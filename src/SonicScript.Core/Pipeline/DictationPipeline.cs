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
}

/// <summary>Transkription fehlgeschlagen. Die Aufnahme sollte aufbewahrt werden.</summary>
public sealed class TranscriptionFailedException(string message, Exception inner) : Exception(message, inner);

/// <summary>
/// Audio → Transkript → Filter (Halluzinationen, Uhrzeiten, Wörterbuch) → optional KI-Modus → Ergebnis.
/// Scheitert nur der KI-Schritt, kommt der wortgetreue Text mit Warnung zurück (nichts geht verloren).
/// </summary>
public sealed class DictationPipeline(ITranscriber transcriber, IChatClient? chat)
{
    public async Task<DictationResult> RunAsync(string audioPath, ModeDefinition mode, AppSettings settings, CancellationToken ct)
    {
        TranscriptionResult tr;
        try
        {
            var lang = settings.Transcription.Language == "auto" ? null : settings.Transcription.Language;
            tr = await transcriber.TranscribeAsync(
                new TranscriptionRequest(audioPath, lang, BuildWhisperPrompt(settings.Dictionary.Terms)), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new TranscriptionFailedException(ex.Message, ex);
        }

        var language = tr.Language ?? (settings.Transcription.Language == "auto" ? null : settings.Transcription.Language);
        var dictionary = new DictionaryApplier(settings.Dictionary);
        var verbatim = PostProcess(tr.Text, language, dictionary);

        if (verbatim.Length == 0 || !mode.NeedsLanguageModel)
            return new DictationResult(verbatim, verbatim, language, mode, DictationWarning.None, null);

        if (chat is null)
            return new DictationResult(verbatim, verbatim, language, mode, DictationWarning.ModeNotConfigured, null);

        string raw;
        try
        {
            var system = PromptBuilder.BuildSystemPrompt(mode, language, settings.Dictionary.Terms);
            raw = await chat.CompleteAsync(system, PromptBuilder.BuildUserMessage(verbatim), mode.Temperature, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is RemoteServiceException or HttpRequestException or InvalidOperationException)
        {
            return new DictationResult(verbatim, verbatim, language, mode, DictationWarning.ModeFailed, ex.Message);
        }

        var cleaned = RewriteGuard.Clean(raw);
        var verdict = RewriteGuard.Check(verbatim, cleaned, mode.Kind);
        if (verdict != RewriteVerdict.Ok)
            return new DictationResult(verbatim, verbatim, language, mode, DictationWarning.ModeRejected, verdict.ToString());

        return new DictationResult(verbatim, dictionary.Apply(cleaned), language, mode, DictationWarning.None, null);
    }

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
