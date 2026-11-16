using SonicScript.Core.Hotkeys;
using SonicScript.Core.LanguageModel;
using SonicScript.Core.Modes;
using SonicScript.Core.Pipeline;
using SonicScript.Core.Settings;
using SonicScript.Core.Text;
using SonicScript.Core.Transcription;
using SonicScript.Core.Updates;

namespace SonicScript.Core.Tests;

public class InsertionFitterTests
{
    [Theory]
    [InlineData("Hallo Welt.", null, "Hallo Welt.")]                       // unbekannt → unverändert
    [InlineData("hallo Welt.", "", "Hallo Welt.")]                         // Feldanfang
    [InlineData("das ist gut.", "Fertig. ", "Das ist gut.")]
    [InlineData("Und dann gehen wir.", "Wir essen,", " und dann gehen wir.")]
    [InlineData("Haus am See", "Wir kaufen ein", " Haus am See")]           // Substantiv bleibt groß
    [InlineData("Ich komme.", "Morgen ", "ich komme.")]
    [InlineData("I think so.", "Well,", " I think so.")]                     // "I" bleibt groß
    [InlineData(", oder?", "Gut", ", oder?")]                               // kein Leerzeichen vor Komma
    [InlineData("Zitat", "„", "Zitat")]                                     // kein Leerzeichen nach Anführungszeichen
    [InlineData("neue Zeile", "Ende\n", "Neue Zeile")]
    [InlineData("42 Stück", "Wir brauchen", " 42 Stück")]
    public void Fits(string text, string? before, string expected) =>
        Assert.Equal(expected, InsertionFitter.Fit(text, before));
}

public class EditSelectionTests
{
    private sealed class StubTranscriber(string text) : ITranscriber
    {
        public Task<TranscriptionResult> TranscribeAsync(TranscriptionRequest request, CancellationToken ct) =>
            Task.FromResult(new TranscriptionResult(text, "de"));
    }

    private sealed class StubChat(Func<string, string, string> answer) : IChatClient
    {
        public string? LastUser;
        public Task<string> CompleteAsync(string systemPrompt, string userMessage, double temperature, CancellationToken ct)
        {
            LastUser = userMessage;
            return Task.FromResult(answer(systemPrompt, userMessage));
        }
    }

    [Fact]
    public async Task AppliesSpokenInstructionToSelection()
    {
        var chat = new StubChat((sys, user) => "<text>\nKurz und knapp.\n</text>");
        var r = await new DictationPipeline(new StubTranscriber("mach das kürzer"), chat)
            .RunEditAsync("a.wav", "Ein sehr langer Text mit vielen Wörtern.", new AppSettings(), default);
        Assert.Equal("Kurz und knapp.", r.Final);
        Assert.Equal("mach das kürzer", r.Verbatim);
        Assert.Contains("<instruction>\nmach das kürzer\n</instruction>", chat.LastUser);
        Assert.Contains("Ein sehr langer Text", chat.LastUser);
    }

    [Fact]
    public async Task WithoutLanguageModel_ChangesNothing()
    {
        var r = await new DictationPipeline(new StubTranscriber("mach das kürzer"), null)
            .RunEditAsync("a.wav", "Text", new AppSettings(), default);
        Assert.True(r.IsEmpty);
        Assert.Equal(DictationWarning.ModeNotConfigured, r.Warning);
    }

    [Fact]
    public void PromptMentionsBothParts() =>
        Assert.Contains("<instruction>", PromptBuilder.BuildEditSystemPrompt("de", []));

    [Fact]
    public void EditHotkeyIsInBindingsAndConflictFree()
    {
        var s = new AppSettings();
        Assert.Contains(s.BuildHotkeyBindings(), b => b.Action == HotkeyAction.EditSelection && b.Combo.ToString() == "Ctrl+Alt+.");
        Assert.Empty(HotkeyConflicts.Find(s.BuildHotkeyBindings()));
    }

    [Fact]
    public void MigrationClearsEditHotkeyIfTaken()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, """
        { "schemaVersion": 2, "modes": [ { "id": "x", "kind": "custom", "name": "X", "hotkey": "Ctrl+Alt+." } ] }
        """);
        var s = new SettingsStore(path).Load();
        Assert.True(s.EditHotkey.IsEmpty);
        Assert.Equal(AppSettings.CurrentSchema, s.SchemaVersion);
    }
}

public class UpdateChecksumTests
{
    [Fact]
    public void ParsesSha256Line()
    {
        var hex = new string('a', 64);
        Assert.Equal(hex, UpdateChecker.ParseSha256($"{hex.ToUpperInvariant()}  SonicScript.exe\n"));
        Assert.Null(UpdateChecker.ParseSha256("kaputt"));
    }

    [Fact]
    public void FindsChecksumAsset()
    {
        const string json = """
        {"tag_name":"v1.0.0","html_url":"h","assets":[
          {"name":"SonicScript.exe.sha256","browser_download_url":"https://x/s"},
          {"name":"SonicScript.exe","browser_download_url":"https://x/e"}]}
        """;
        var r = UpdateChecker.Parse(json)!;
        Assert.Equal("https://x/e", r.DownloadUrl);
        Assert.Equal("https://x/s", r.ChecksumUrl);
    }
}
