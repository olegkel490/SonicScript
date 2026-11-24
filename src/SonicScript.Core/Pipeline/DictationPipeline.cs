using SonicScript.Core.Audio;
using SonicScript.Core.LanguageModel;
using SonicScript.Core.Modes;
using SonicScript.Core.Net;
using SonicScript.Core.Settings;
using SonicScript.Core.Text;
using SonicScript.Core.Transcription;

namespace SonicScript.Core.Pipeline;

public enum DictationWarning
{
    None,
    /// <summary>Sprachmodell nicht erreichbar/fehlerhaft → wortgetreuer Text eingefügt.</summary>
    ModeFailed,
    /// <summary>Antwort des Sprachmodells verworfen (leer, zu lang, Antwort statt Text) → wortgetreuer Text.</summary>
    ModeRejected,
    /// <summary>Für den Modus ist kein Sprachmodell eingerichtet → wortgetreuer Text.</summary>
    ModeNotConfigured,
}

public sealed record DictationResult(
    string Verbatim,
    string Final,
    string? Language,
    ModeDefinition Mode,
    DictationWarning Warning,
    string? WarningDetail)
{
    public bool IsEmpty => Final.Length == 0;

    /// <summary>Per Sprachbefehl ("Notiz: …") als Notiz ablegen statt einfügen.</summary>
    public bool SaveAsNote { get; init; }

    /// <summary>Erkanntes Stichwort, falls der Modus per Sprachbefehl gewählt wurde.</summary>
    public string? Keyword { get; init; }

    /// <summary>Verworfene KI-Antwort (zum Nachträglich-Einfügen anbieten).</summary>
    public string? RejectedText { get; init; }

    /// <summary>Cloud-Erkennung fiel aus, lokal erkannt.</summary>
    public bool UsedFallbackTranscriber { get; init; }

    /// <summary>Hauptmodell fiel aus, Ersatzmodell hat geantwortet.</summary>
    public bool UsedFallbackModel { get; init; }
}

/// <summary>Transkription fehlgeschlagen. Die Aufnahme sollte aufbewahrt werden.</summary>
public sealed class TranscriptionFailedException(string message, Exception inner) : Exception(message, inner);

/// <summary>
/// Audio → Transkript → Filter (Halluzinationen, Uhrzeiten, Wörterbuch) → optional KI-Modus → Ergebnis.
/// Scheitert nur der KI-Schritt, kommt der wortgetreue Text mit Warnung zurück (nichts geht verloren).
/// </summary>
/// <param name="fallbackChat">Ersatzmodell, wenn das Hauptmodell (nach einer Wiederholung) scheitert oder verworfen wird.</param>
/// <param name="fallbackTranscriber">Ersatz-Erkennung (z. B. lokal), wenn die Erkennung fehlschlägt.</param>
public sealed class DictationPipeline(ITranscriber transcriber, IChatClient? chat, IChatClient? fallbackChat = null, ITranscriber? fallbackTranscriber = null)
{
    /// <param name="allowKeywords">Sprachbefehle am Anfang auswerten (nur beim normalen Diktat).</param>
    public async Task<DictationResult> RunAsync(string audioPath, ModeDefinition mode, AppSettings settings, CancellationToken ct, bool allowKeywords = false, string? replyTo = null)
    {
        var tr = await TranscribeAsync(audioPath, settings, ct).ConfigureAwait(false);
        return await RunTranscriptAsync(tr, mode, settings, ct, allowKeywords, replyTo).ConfigureAwait(false);
    }

    /// <summary>Wie <see cref="RunAsync"/>, aber mit bereits erkanntem Text (z. B. aus der Live-Erkennung). null = Stille.</summary>
    /// <param name="replyTo">Für den Antwort-Modus: die E-Mail, auf die geantwortet wird (null = wie Mail-Modus).</param>
    public async Task<DictationResult> RunTranscriptAsync(TranscriptionResult? tr, ModeDefinition mode, AppSettings settings, CancellationToken ct, bool allowKeywords = false, string? replyTo = null)
    {
        if (tr is null) return new DictationResult("", "", null, mode, DictationWarning.None, null); // nur Stille

        var language = tr.Language ?? (settings.Transcription.Language == "auto" ? null : settings.Transcription.Language);
        var dictionary = new DictionaryApplier(settings.Dictionary);
        var verbatim = PostProcess(tr.Text, language, dictionary);

        if (allowKeywords && settings.VoiceKeywords && ModeKeywords.Detect(verbatim, settings) is { } kw)
        {
            var r = await RewriteAsync(kw.Rest, language, kw.Mode, settings, dictionary, ct, replyTo).ConfigureAwait(false);
            return r with { SaveAsNote = kw.IsNote, Keyword = kw.Keyword, UsedFallbackTranscriber = tr.Fallback };
        }
        var result = await RewriteAsync(verbatim, language, mode, settings, dictionary, ct, replyTo).ConfigureAwait(false);
        return result with { UsedFallbackTranscriber = tr.Fallback };
    }

    /// <summary>Nur erkennen (Stille kürzen, ggf. Ersatz-Erkennung). null = keine Sprache.</summary>
    public Task<TranscriptionResult?> TranscribeFileAsync(string audioPath, AppSettings settings, CancellationToken ct) =>
        TranscribeAsync(audioPath, settings, ct);

    /// <summary>Bereits erkannten Text (z. B. aus dem Verlauf) erneut durch einen Modus schicken.</summary>
    public Task<DictationResult> RewriteTextAsync(string verbatim, string? language, ModeDefinition mode, AppSettings settings, CancellationToken ct) =>
        RewriteAsync(verbatim, language, mode, settings, new DictionaryApplier(settings.Dictionary), ct);

    private sealed record Attempt(string? Cleaned, string? Error, RewriteVerdict Verdict, bool ByFallback);

    /// <summary>
    /// Sprachmodell fragen, mit Absicherung: Bei vorübergehenden Fehlern (Zeitüberschreitung, Serverfehler, leer)
    /// einmal wiederholen; scheitert es dann noch oder wird die Antwort verworfen, das Ersatzmodell fragen.
    /// </summary>
    private async Task<Attempt> CompleteGuardedAsync(string system, string user, double temperature, string input, ModeKind kind, CancellationToken ct)
    {
        async Task<Attempt> Try(IChatClient client, bool byFallback)
        {
            try
            {
                var raw = await client.CompleteAsync(system, user, temperature, ct).ConfigureAwait(false);
                var cleaned = RewriteGuard.Clean(raw);
                return new Attempt(cleaned, null, RewriteGuard.Check(input, cleaned, kind), byFallback);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is RemoteServiceException or HttpRequestException or InvalidOperationException or OperationCanceledException)
            {
                return new Attempt(null, ex.Message, RewriteVerdict.Ok, byFallback);
            }
        }

        var a = await Try(chat!, false).ConfigureAwait(false);
        if (a.Error is null && a.Verdict == RewriteVerdict.Ok) return a;
        if ((a.Error is not null && IsTransient(a.Error)) || a.Verdict == RewriteVerdict.Empty)
        {
            var again = await Try(chat!, false).ConfigureAwait(false);
            if (again.Error is null && again.Verdict == RewriteVerdict.Ok) return again;
            a = again;
        }
        if (fallbackChat is not null)
        {
            var fb = await Try(fallbackChat, true).ConfigureAwait(false);
            if (fb.Error is null && fb.Verdict == RewriteVerdict.Ok) return fb;
            // Ersatzmodell auch erfolglos: den aussagekräftigeren Grund melden (Antwort vor Fehler).
            if (a.Error is not null && fb.Error is null) a = fb;
        }
        return a;
    }

    /// <summary>Fehler, bei denen eine Wiederholung Sinn ergibt.</summary>
    public static bool IsTransient(string error) =>
        error.StartsWith("timeout", StringComparison.Ordinal) || error.StartsWith("unreachable", StringComparison.Ordinal) ||
        error.StartsWith("http_5", StringComparison.Ordinal) || error.StartsWith("http_429", StringComparison.Ordinal) ||
        error.StartsWith("output_limit", StringComparison.Ordinal) || error.StartsWith("bad_response", StringComparison.Ordinal) ||
        error.Contains("canceled", StringComparison.OrdinalIgnoreCase);

    private async Task<DictationResult> RewriteAsync(string verbatim, string? language, ModeDefinition mode, AppSettings settings, DictionaryApplier dictionary, CancellationToken ct, string? replyTo = null)
    {
        if (verbatim.Length == 0 || !mode.NeedsLanguageModel)
            return new DictationResult(verbatim, verbatim, language, mode, DictationWarning.None, null);

        if (chat is null)
            return new DictationResult(verbatim, verbatim, language, mode, DictationWarning.ModeNotConfigured, null);

        var reply = mode.Kind == ModeKind.Reply && !string.IsNullOrWhiteSpace(replyTo);
        var system = reply
            ? PromptBuilder.BuildReplySystemPrompt(mode, language, settings.Dictionary.Terms)
            : PromptBuilder.BuildSystemPrompt(mode, language, settings.Dictionary.Terms);
        var user = reply ? PromptBuilder.BuildReplyUserMessage(replyTo!, verbatim) : PromptBuilder.BuildUserMessage(verbatim);
        var a = await CompleteGuardedAsync(system, user, mode.Temperature, verbatim, mode.Kind, ct).ConfigureAwait(false);

        if (a.Error is not null)
            return new DictationResult(verbatim, verbatim, language, mode, DictationWarning.ModeFailed, a.Error);
        if (a.Verdict != RewriteVerdict.Ok)
            return new DictationResult(verbatim, verbatim, language, mode, DictationWarning.ModeRejected, a.Verdict.ToString())
            {
                RejectedText = string.IsNullOrWhiteSpace(a.Cleaned) ? null : dictionary.Apply(a.Cleaned),
            };

        return new DictationResult(verbatim, dictionary.Apply(a.Cleaned!), language, mode, DictationWarning.None, null) { UsedFallbackModel = a.ByFallback };
    }

    /// <summary>
    /// "Auswahl bearbeiten": Anweisung transkribieren und auf <paramref name="selectedText"/> anwenden.
    /// Scheitert das Sprachmodell, ist <see cref="DictationResult.Final"/> leer (die Markierung bleibt unverändert).
    /// </summary>
    public async Task<DictationResult> RunEditAsync(string audioPath, string selectedText, AppSettings settings, CancellationToken ct)
    {
        var tr = await TranscribeAsync(audioPath, settings, ct).ConfigureAwait(false);
        return await RunEditTranscriptAsync(tr, selectedText, settings, ct).ConfigureAwait(false);
    }

    public async Task<DictationResult> RunEditTranscriptAsync(TranscriptionResult? tr, string selectedText, AppSettings settings, CancellationToken ct)
    {
        var mode = EditMode;
        if (tr is null) return new DictationResult("", "", null, mode, DictationWarning.None, null);

        var language = tr.Language ?? (settings.Transcription.Language == "auto" ? null : settings.Transcription.Language);
        var instruction = HallucinationFilter.Clean(tr.Text);
        if (instruction.Length == 0) return new DictationResult("", "", language, mode, DictationWarning.None, null);
        if (chat is null) return new DictationResult(instruction, "", language, mode, DictationWarning.ModeNotConfigured, null);

        var a = await CompleteGuardedAsync(
            PromptBuilder.BuildEditSystemPrompt(language, settings.Dictionary.Terms),
            PromptBuilder.BuildEditUserMessage(selectedText, instruction), 0.3, selectedText, ModeKind.Custom, ct).ConfigureAwait(false);
        if (a.Error is not null)
            return new DictationResult(instruction, "", language, mode, DictationWarning.ModeFailed, a.Error);
        if (a.Verdict != RewriteVerdict.Ok)
            return new DictationResult(instruction, "", language, mode, DictationWarning.ModeRejected, a.Verdict.ToString());
        return new DictationResult(instruction, a.Cleaned!, language, mode, DictationWarning.None, null) { UsedFallbackModel = a.ByFallback };
    }

    /// <summary>
    /// Stille kürzen (falls eingeschaltet) und transkribieren. null = in der Aufnahme war keine Sprache.
    /// Die gekürzte Zwischendatei wird danach gelöscht.
    /// </summary>
    private async Task<TranscriptionResult?> TranscribeAsync(string audioPath, AppSettings settings, CancellationToken ct)
    {
        var trim = settings.TrimSilence ? SilenceTrimmer.Trim(audioPath) : null;
        if (trim is { HasSpeech: false }) return null;
        var path = trim?.Path ?? audioPath;
        var lang = settings.Transcription.Language == "auto" ? null : settings.Transcription.Language;
        var request = new TranscriptionRequest(path, lang, BuildWhisperPrompt(settings.Dictionary.Terms));
        try
        {
            return await transcriber.TranscribeAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (fallbackTranscriber is null) throw new TranscriptionFailedException(ex.Message, ex);
            try
            {
                var r = await fallbackTranscriber.TranscribeAsync(request, ct).ConfigureAwait(false);
                return r with { Fallback = true };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                throw new TranscriptionFailedException(ex.Message, ex); // ursprünglichen Fehler melden
            }
        }
        finally
        {
            if (trim is { Changed: true } && trim.Path != audioPath)
            {
                try { File.Delete(trim.Path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    public static readonly ModeDefinition EditMode = new() { Id = "edit", Kind = ModeKind.Custom, Name = "Bearbeiten", Symbol = "✂" };

    public static string PostProcess(string text, string? language, DictionaryApplier dictionary)
    {
        var t = HallucinationFilter.Clean(text);
        if (t.Length == 0) return "";
        if (language is null || PromptBuilder.IsGerman(language))
            t = SpokenTimeNormalizer.Normalize(t);
        return dictionary.Apply(t).Trim();
    }

    /// <summary>Whisper-"initial prompt": Begriffe als Schreibhilfe (max. ~200 Zeichen, sonst halluziniert Whisper eher).</summary>
    public static string? BuildWhisperPrompt(IReadOnlyList<string> terms)
    {
        var parts = terms.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
        if (parts.Count == 0) return null;
        var sb = new System.Text.StringBuilder();
        foreach (var p in parts)
        {
            if (sb.Length + p.Length + 2 > 200) break;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(p);
        }
        return sb.Length == 0 ? null : sb.ToString();
    }
}
