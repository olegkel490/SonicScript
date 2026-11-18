using SonicScript.Core.Audio;
using SonicScript.Core.Hotkeys;
using SonicScript.Core.LanguageModel;
using SonicScript.Core.Modes;
using SonicScript.Core.Net;
using SonicScript.Core.Pipeline;
using SonicScript.Core.Settings;
using SonicScript.Core.Stats;
using SonicScript.Core.Text;
using SonicScript.Core.Transcription;

namespace SonicScript.Core.Tests;

public class LiveTranscriptionTests
{
    private const int Rate = 16000;

    /// <summary>Gibt die Länge des Audios in ganzen Sekunden als Text zurück ("s5").</summary>
    private sealed class DurationTranscriber(Exception? fail = null) : ITranscriber
    {
        public int Calls;
        public int Concurrent;
        public int MaxConcurrent;
        public async Task<TranscriptionResult> TranscribeAsync(TranscriptionRequest request, CancellationToken ct)
        {
            var c = Interlocked.Increment(ref Concurrent);
            MaxConcurrent = Math.Max(MaxConcurrent, c);
            Interlocked.Increment(ref Calls);
            try
            {
                await Task.Delay(5, ct);
                if (fail is not null) throw fail;
                var d = WavFile.GetDuration(request.AudioPath)!.Value;
                return new TranscriptionResult($"s{Math.Round(d.TotalSeconds)}.", "de");
            }
            finally { Interlocked.Decrement(ref Concurrent); }
        }
    }

    private static short[] Audio(params (double Seconds, bool Loud)[] parts)
    {
        var rnd = new Random(2);
        var list = new List<short>();
        foreach (var (sec, loud) in parts)
            for (var i = 0; i < (int)(sec * Rate); i++)
                list.Add(loud ? (short)(Math.Sin(2 * Math.PI * 300 * i / Rate) * 6000) : (short)rnd.Next(-20, 20));
        return list.ToArray();
    }

    private static LiveTranscriptionOptions Options(TempDir dir, bool preview = false) => new()
    {
        WorkDirectory = dir.Path,
        ChunkTarget = TimeSpan.FromSeconds(4),
        ChunkMax = TimeSpan.FromSeconds(8),
        Tick = TimeSpan.FromMilliseconds(10),
        Preview = preview,
        PreviewInterval = TimeSpan.FromSeconds(1),
        TrimSilence = false,
    };

    private static async Task Feed(LiveTranscription live, short[] audio, int waitEveryMs = 0)
    {
        // in 1-s-Paketen wie vom Mikrofon
        for (var i = 0; i < audio.Length; i += Rate)
        {
            live.Append(audio.AsSpan(i, Math.Min(Rate, audio.Length - i)));
            if (waitEveryMs > 0) await Task.Delay(waitEveryMs);
        }
    }

    private static async Task WaitFor(Func<bool> cond)
    {
        for (var i = 0; i < 1000 && !cond(); i++) await Task.Delay(10);
    }

    [Fact]
    public async Task SplitsLongDictationAtPausesDuringRecording()
    {
        using var dir = new TempDir();
        var t = new DurationTranscriber();
        using var live = new LiveTranscription(t, Rate, Options(dir));
        // 4 s Sprache, 1 s Pause, 4 s Sprache, 1 s Pause, 2 s Sprache
        var audio = Audio((4, true), (1, false), (4, true), (1, false), (2, true));
        await Feed(live, audio, 30);
        await WaitFor(() => live.CommittedChunks >= 2);
        Assert.True(live.CommittedChunks >= 2);

        var r = await live.FinishAsync(default);
        Assert.NotNull(r);
        Assert.Equal("de", r.Language);
        var parts = r.Text.Split(' ');
        Assert.Equal(3, parts.Length);
        // Schnitte in der Mitte der Pausen: ~4,5 s + ~5 s + ~2,5 s
        Assert.Equal(12, parts.Sum(p => int.Parse(p.TrimStart('s').TrimEnd('.'))), 1.0);
        Assert.Equal(1, t.MaxConcurrent);
        Assert.Empty(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public async Task ShortDictationIsTranscribedOnceAtTheEnd()
    {
        using var dir = new TempDir();
        var t = new DurationTranscriber();
        using var live = new LiveTranscription(t, Rate, Options(dir));
        await Feed(live, Audio((2, true)));
        var r = await live.FinishAsync(default);
        Assert.Equal("s2.", r!.Text);
        Assert.Equal(1, t.Calls);
    }

    [Fact]
    public async Task CutsAtMaximumWithoutPause()
    {
        using var dir = new TempDir();
        using var live = new LiveTranscription(new DurationTranscriber(), Rate, Options(dir));
        await Feed(live, Audio((10, true)), 30);
        await WaitFor(() => live.CommittedChunks >= 1);
        var r = await live.FinishAsync(default);
        Assert.Equal(2, r!.Text.Split(' ').Length);
    }

    [Fact]
    public async Task PreviewShowsTextWhileRecording()
    {
        using var dir = new TempDir();
        using var live = new LiveTranscription(new DurationTranscriber(), Rate, Options(dir, preview: true));
        var previews = new List<string>();
        live.PreviewChanged += p => { lock (previews) previews.Add(p); };
        await Feed(live, Audio((3, true)), 40);
        await WaitFor(() => { lock (previews) return previews.Count > 0; });
        lock (previews) Assert.NotEmpty(previews);
        Assert.NotNull(await live.FinishAsync(default));
    }

    [Fact]
    public async Task SilenceOnlyGivesNull()
    {
        using var dir = new TempDir();
        var o = Options(dir);
        using var live = new LiveTranscription(new DurationTranscriber(), Rate, new LiveTranscriptionOptions
        {
            WorkDirectory = o.WorkDirectory, TrimSilence = true, Tick = o.Tick,
        });
        await Feed(live, Audio((2, false)));
        Assert.Null(await live.FinishAsync(default));
    }

    [Fact]
    public async Task FailedChunkMakesFinishThrow()
    {
        using var dir = new TempDir();
        using var live = new LiveTranscription(new DurationTranscriber(new InvalidOperationException("kaputt")), Rate, Options(dir));
        await Feed(live, Audio((4, true), (1, false), (2, true)), 30);
        await Task.Delay(200);
        await Assert.ThrowsAsync<InvalidOperationException>(() => live.FinishAsync(default));
    }
}

public class CorrectionDetectorTests
{
    [Fact]
    public void FindsSingleWordCorrection()
    {
        var r = CorrectionDetector.Detect("Wir nutzen Opnrauter für die Modelle.", "Hallo,\nWir nutzen OpenRouter für die Modelle.\nGruß");
        Assert.Equal([new CorrectionSuggestion("Opnrauter", "OpenRouter")], r);
    }

    [Fact]
    public void FindsCaseFixAndJoinedWords()
    {
        var r = CorrectionDetector.Detect("Bitte in open router und in sonicscript eintragen", "Bitte in OpenRouter und in SonicScript eintragen");
        Assert.Contains(new CorrectionSuggestion("open router", "OpenRouter"), r);
        Assert.Contains(new CorrectionSuggestion("sonicscript", "SonicScript"), r);
    }

    [Theory]
    [InlineData("Das Meeting ist am Montag um zehn.", "Das Meeting ist am Montag um zehn.")]                // nichts geändert
    [InlineData("Das Meeting ist am Montag um zehn.", "Das Meeting ist am Dienstag um zehn.")]              // anderes Wort, kein Tippfehler
    [InlineData("das Meeting ist am Montag um zehn.", "Das Meeting ist am Montag um zehn.")]                // nur Satzanfang groß
    [InlineData("Das Meeting ist am Montag um zehn.", "Komplett anderer Text, ganz neu geschrieben worden.")] // umformuliert
    [InlineData("Das Meeting ist am Montag um zehn.", "")]
    public void IgnoresNonCorrections(string inserted, string current) =>
        Assert.Empty(CorrectionDetector.Detect(inserted, current));

    [Fact]
    public void AcceptAddsReplacementAndTerm()
    {
        var d = new DictionarySettings();
        d.Accept("Opnrauter", "OpenRouter");
        d.Accept("sonicscript", "SonicScript");
        Assert.Contains("OpenRouter", d.Terms);
        Assert.Contains("SonicScript", d.Terms);
        Assert.Single(d.Replacements); // Groß/klein regelt der Begriff
        Assert.Equal("Opnrauter", d.Replacements[0].From);
    }
}

public class UsageStatsTests
{
    [Fact]
    public void SumsPerPeriodAndPersists()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "stats.json");
        var stats = new UsageStats(path);
        var today = new DateOnly(2026, 11, 18);
        stats.Add(today, "mail", string.Join(" ", Enumerable.Repeat("Wort", 80)), TimeSpan.FromSeconds(30));
        stats.Add(today, "mail", "eins zwei drei", TimeSpan.FromSeconds(3));
        stats.Add(today.AddDays(-10), "improve", "vier fünf", TimeSpan.FromSeconds(2));

        var day = new UsageStats(path).Summary(today, today);
        Assert.Equal(2, day.Dictations);
        Assert.Equal(83, day.Words);
        Assert.Equal(33, day.Spoken.TotalSeconds, 3);
        // 83 Wörter / 40 pro Minute = 124,5 s Tippen − 33 s Sprechen
        Assert.Equal(91.5, day.Saved.TotalSeconds, 1);
        Assert.Equal(("mail", 2), day.TopModes[0]);

        var all = stats.Summary(null, today);
        Assert.Equal(3, all.Dictations);
        stats.Clear();
        Assert.Equal(0, stats.Summary(null, today).Dictations);
    }

    [Fact]
    public void BrokenFileStartsEmpty()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "stats.json");
        File.WriteAllText(path, "{kaputt");
        Assert.Equal(0, new UsageStats(path).Summary(null, new DateOnly(2026, 1, 1)).Dictations);
    }
}

public class OpenRouterUsageTests
{
    [Fact]
    public void ParsesKeyInfo()
    {
        var u = OpenRouterUsage.Parse("""{"data":{"label":"x","usage":12.5,"usage_daily":0.25,"usage_weekly":1.5,"usage_monthly":4.75,"limit":20,"limit_remaining":7.5}}""");
        Assert.Equal(new OpenRouterKeyUsage(12.5, 0.25, 1.5, 4.75, 20, 7.5), u);
        var old = OpenRouterUsage.Parse("""{"data":{"usage":3,"limit":null}}""");
        Assert.Null(old.Month);
        Assert.Throws<RemoteServiceException>(() => OpenRouterUsage.Parse("nope"));
    }

    [Fact]
    public void SumsKeys()
    {
        var s = OpenRouterUsage.Sum([new(1, 0.1, 0.5, 1, 10, 9), new(2, null, 1, 2, null, null)]);
        Assert.Equal(3, s.Total);
        Assert.Equal(0.1, s.Day);
        Assert.Equal(3, s.Month);
        Assert.Null(s.Limit);
    }

    [Theory]
    [InlineData("https://openrouter.ai/api/v1", true)]
    [InlineData("https://api.openai.com/v1", false)]
    [InlineData("", false)]
    public void DetectsOpenRouter(string url, bool expected) => Assert.Equal(expected, OpenRouterUsage.IsOpenRouter(url));
}

public class ReplyModeTests
{
    private sealed class StubTranscriber(string text) : ITranscriber
    {
        public Task<TranscriptionResult> TranscribeAsync(TranscriptionRequest request, CancellationToken ct) =>
            Task.FromResult(new TranscriptionResult(text, "de"));
    }

    private sealed class CapturingChat(string answer) : IChatClient
    {
        public string? System, User;
        public Task<string> CompleteAsync(string systemPrompt, string userMessage, double temperature, CancellationToken ct)
        {
            System = systemPrompt;
            User = userMessage;
            return Task.FromResult(answer);
        }
    }

    [Fact]
    public async Task UsesMailAsContext()
    {
        var s = new AppSettings();
        var chat = new CapturingChat("Hi John,\n\nThursday works for me.\n\nBest regards");
        var r = await new DictationPipeline(new StubTranscriber("x"), chat).RunTranscriptAsync(
            new TranscriptionResult("sag zu, Donnerstag passt", "de"), s.FindMode(BuiltInModes.ReplyId)!, s, default,
            replyTo: "Hi, does Thursday work for you? John");
        Assert.Contains("SPRACHE DER E-MAIL", chat.System);
        Assert.Contains("<mail>\nHi, does Thursday work for you? John\n</mail>", chat.User);
        Assert.Contains("sag zu, Donnerstag passt", chat.User);
        Assert.Equal(DictationWarning.None, r.Warning);
        Assert.StartsWith("Hi John,", r.Final);
    }

    [Fact]
    public async Task WithoutMailBehavesLikeMailMode()
    {
        var s = new AppSettings();
        var chat = new CapturingChat("Hallo,\n\nich komme.\n\nViele Grüße");
        await new DictationPipeline(new StubTranscriber("x"), chat).RunTranscriptAsync(
            new TranscriptionResult("ich komme", "de"), s.FindMode(BuiltInModes.ReplyId)!, s, default);
        Assert.Equal(PromptBuilder.BuildSystemPrompt(s.FindMode(BuiltInModes.MailId)!, "de", []), chat.System);
        Assert.DoesNotContain("<mail>", chat.User);
    }

    [Fact]
    public void LongReplyToShortDictationIsAccepted()
    {
        var verdict = RewriteGuard.Check("sag zu", new string('x', 800), ModeKind.Reply);
        Assert.Equal(RewriteVerdict.Ok, verdict);
    }
}

public class UndoHotkeyTests
{
    [Fact]
    public void DefaultIsCtrlAltZAndBound()
    {
        var s = new AppSettings();
        Assert.Equal(new KeyCombo(KeyModifiers.Ctrl | KeyModifiers.Alt, 'Z'), s.UndoHotkey);
        Assert.Contains(s.BuildHotkeyBindings(), b => b.Id == "undo" && b.Action == HotkeyAction.Undo);
    }

    [Fact]
    public void MigrationDropsUndoKeyIfTaken()
    {
        var s = new AppSettings { SchemaVersion = 5, DictateHotkey = AppSettings.DefaultUndoHotkey };
        s.Normalize();
        Assert.Equal(KeyCombo.None, s.UndoHotkey);
        Assert.Equal(AppSettings.CurrentSchema, s.SchemaVersion);
    }

    [Fact]
    public void UndoIsCommandWithoutRecording()
    {
        var s = new AppSettings();
        var m = new HotkeyMatcher(s.BuildHotkeyBindings());
        m.OnKey(0x11, true); // Strg
        m.OnKey(0x12, true); // Alt
        var r = m.OnKey('Z', true);
        Assert.True(r.Swallow);
        Assert.Equal(HotkeyEventKind.Undo, r.Event!.Value.Kind);
        Assert.Null(m.ActiveBinding);
    }
}
