using SonicScript.Core.Audio;
using SonicScript.Core.Modes;
using SonicScript.Core.Pipeline;
using SonicScript.Core.Settings;
using SonicScript.Core.Transcription;

namespace SonicScript.Core.Tests;

public class SilenceTrimmerTests
{
    private const int Rate = 16000;

    /// <summary>Segmente: (Sekunden, laut?) – laut = 440-Hz-Ton, leise = leichtes Rauschen.</summary>
    private static string MakeWav(TempDir dir, params (double Seconds, bool Loud)[] parts)
    {
        var rnd = new Random(1);
        var samples = new List<short>();
        foreach (var (sec, loud) in parts)
        {
            var n = (int)(sec * Rate);
            for (var i = 0; i < n; i++)
                samples.Add(loud ? (short)(Math.Sin(2 * Math.PI * 440 * i / Rate) * 8000) : (short)rnd.Next(-30, 30));
        }
        var path = Path.Combine(dir.Path, $"t-{Guid.NewGuid():N}.wav");
        PcmWav.Write(path, samples.ToArray(), Rate);
        return path;
    }

    [Fact]
    public void TrimsLeadingTrailingSilenceAndShortensLongPauses()
    {
        using var dir = new TempDir();
        var wav = MakeWav(dir, (2.0, false), (1.0, true), (4.0, false), (1.0, true), (3.0, false));
        var r = SilenceTrimmer.Trim(wav);
        Assert.True(r.HasSpeech);
        Assert.True(r.Changed);
        Assert.NotEqual(wav, r.Path);
        Assert.Equal(11.0, r.Original.TotalSeconds, 1);
        // 2 s Sprache + 2×0,25 s Rand + Pause auf 0,5 s (+ Ränder) ≈ 3,5 s statt 11 s
        Assert.InRange(r.Trimmed.TotalSeconds, 2.8, 4.2);
        Assert.Equal(r.Trimmed.TotalSeconds, WavFile.GetDuration(r.Path)!.Value.TotalSeconds, 2);
    }

    [Fact]
    public void KeepsShortNaturalPauses()
    {
        using var dir = new TempDir();
        var wav = MakeWav(dir, (1.0, true), (0.6, false), (1.0, true));
        var r = SilenceTrimmer.Trim(wav);
        Assert.False(r.Changed);          // nichts Lohnendes zu kürzen
        Assert.Equal(wav, r.Path);
    }

    [Fact]
    public void PureSilenceHasNoSpeech()
    {
        using var dir = new TempDir();
        var r = SilenceTrimmer.Trim(MakeWav(dir, (3.0, false)));
        Assert.False(r.HasSpeech);
    }

    [Fact]
    public void BrokenOrMissingFilesAreLeftAlone()
    {
        using var dir = new TempDir();
        var bad = Path.Combine(dir.Path, "bad.wav");
        File.WriteAllText(bad, "kein wav");
        Assert.Equal(bad, SilenceTrimmer.Trim(bad).Path);
        Assert.True(SilenceTrimmer.Trim(Path.Combine(dir.Path, "fehlt.wav")).HasSpeech);
    }

    private sealed class CapturingTranscriber : ITranscriber
    {
        public string? Path;
        public TimeSpan? Duration;
        public Task<TranscriptionResult> TranscribeAsync(TranscriptionRequest request, CancellationToken ct)
        {
            Path = request.AudioPath;
            Duration = WavFile.GetDuration(request.AudioPath);
            return Task.FromResult(new TranscriptionResult("Hallo", "de"));
        }
    }

    [Fact]
    public async Task PipelineTranscribesTrimmedAudioAndCleansUp()
    {
        using var dir = new TempDir();
        var wav = MakeWav(dir, (3.0, false), (1.0, true), (3.0, false));
        var t = new CapturingTranscriber();
        var s = new AppSettings();
        var r = await new DictationPipeline(t, null).RunAsync(wav, s.DefaultMode, s, default);
        Assert.Equal("Hallo", r.Final);
        Assert.NotEqual(wav, t.Path);
        Assert.True(t.Duration!.Value.TotalSeconds < 2);
        Assert.False(File.Exists(t.Path));   // Zwischendatei gelöscht
        Assert.True(File.Exists(wav));       // Original bleibt (z. B. für "Aufnahme aufbewahren")
    }

    [Fact]
    public async Task PipelineSkipsTranscriptionForSilence()
    {
        using var dir = new TempDir();
        var t = new CapturingTranscriber();
        var s = new AppSettings();
        var r = await new DictationPipeline(t, null).RunAsync(MakeWav(dir, (2.0, false)), s.DefaultMode, s, default);
        Assert.True(r.IsEmpty);
        Assert.Null(t.Path);
    }

    [Fact]
    public async Task TrimmingCanBeTurnedOff()
    {
        using var dir = new TempDir();
        var wav = MakeWav(dir, (3.0, false), (1.0, true), (3.0, false));
        var t = new CapturingTranscriber();
        var s = new AppSettings { TrimSilence = false };
        await new DictationPipeline(t, null).RunAsync(wav, s.DefaultMode, s, default);
        Assert.Equal(wav, t.Path);
    }
}

public class SettingsTransferTests
{
    [Fact]
    public void ExportOmitsDeviceSpecificValuesAndSecrets()
    {
        var s = new AppSettings { FirstRunCompleted = true, Autostart = true };
        s.Transcription.MicrophoneDevice = 3;
        s.Dictionary.Terms.Add("OpenRouter");
        var json = SettingsTransfer.Export(s, "0.7.0");
        Assert.Contains(SettingsTransfer.Marker, json);
        Assert.Contains("OpenRouter", json);
        Assert.DoesNotContain("microphoneDevice", json);
        Assert.DoesNotContain("firstRunCompleted", json);
        Assert.DoesNotContain("localModelId", json);
    }

    [Fact]
    public void ImportTakesSharedSettingsButKeepsThisMachinesValues()
    {
        var source = new AppSettings { SchemaVersion = AppSettings.CurrentSchema, CurrentModeId = "mail" };
        source.Dictionary.Terms.Add("n8n");
        source.Modes.Add(new ModeDefinition { Id = "x", Name = "Angebot", Instruction = "Als Angebot" });
        source.Notes.Folder = @"Z:\gibt\es\nicht";
        source.LanguageModel.Endpoint.Model = "google/gemini-3.1-flash-lite";
        var json = SettingsTransfer.Export(source, "0.7.0");

        var here = new AppSettings { FirstRunCompleted = true };
        here.Transcription.MicrophoneDevice = 2;
        here.Transcription.LocalModelId = "small";
        here.Notes.Folder = @"C:\Notizen";

        var result = SettingsTransfer.Import(json, here, _ => false);
        Assert.Contains("n8n", result.Dictionary.Terms);
        Assert.NotNull(result.FindMode("x"));
        Assert.Equal("mail", result.CurrentModeId);
        Assert.Equal("google/gemini-3.1-flash-lite", result.LanguageModel.Endpoint.Model);
        Assert.True(result.FirstRunCompleted);
        Assert.Equal(2, result.Transcription.MicrophoneDevice);
        Assert.Equal("small", result.Transcription.LocalModelId);
        Assert.Equal(@"C:\Notizen", result.Notes.Folder);   // Ordner von der Quelle existiert hier nicht
    }

    [Fact]
    public void ImportAcceptsPlainSettingsFileAndRejectsGarbage()
    {
        using var dir = new TempDir();
        var store = new SettingsStore(Path.Combine(dir.Path, "settings.json"));
        var s = new AppSettings { DefaultModeId = "improve" };
        store.Save(s);
        var r = SettingsTransfer.Import(File.ReadAllText(store.Path), new AppSettings());
        Assert.Equal("improve", r.DefaultModeId);

        Assert.Throws<FormatException>(() => SettingsTransfer.Import("{ kaputt", new AppSettings()));
        Assert.Throws<FormatException>(() => SettingsTransfer.Import("[1,2]", new AppSettings()));
    }
}

public class MailEnglishModeTests
{
    [Fact]
    public void IsBuiltInRightAfterMail()
    {
        var ids = new AppSettings().Modes.Select(m => m.Id).ToList();
        Assert.Equal(ids.IndexOf(BuiltInModes.MailId) + 1, ids.IndexOf(BuiltInModes.MailEnglishId));
    }

    [Theory]
    [InlineData("de", "AUF ENGLISCH")]
    [InlineData("en", "IN ENGLISH")]
    [InlineData("fr", "IN ENGLISH")]
    public void PromptAsksForEnglishMail(string lang, string expected)
    {
        var p = PromptBuilder.BuildSystemPrompt(new AppSettings().FindMode(BuiltInModes.MailEnglishId)!, lang, []);
        Assert.Contains(expected, p);
        Assert.DoesNotContain("Keep the language", p);
    }

    [Fact]
    public void LongestKeywordWins()
    {
        var s = new AppSettings();
        Assert.Equal(BuiltInModes.MailEnglishId, ModeKeywords.Detect("Mail Englisch: Hallo Anna", s)!.Mode.Id);
        Assert.Equal(BuiltInModes.MailId, ModeKeywords.Detect("Mail: Hallo Anna", s)!.Mode.Id);
    }
}
