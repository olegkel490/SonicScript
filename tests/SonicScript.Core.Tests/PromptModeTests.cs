using SonicScript.Core.LanguageModel;
using SonicScript.Core.Modes;
using SonicScript.Core.Pipeline;
using SonicScript.Core.Rules;
using SonicScript.Core.Settings;
using SonicScript.Core.Transcription;

namespace SonicScript.Core.Tests;

public class ModeKeywordTests
{
    private static readonly AppSettings S = Custom();

    private static AppSettings Custom()
    {
        var s = new AppSettings();
        s.Modes.Add(new ModeDefinition { Id = "mail", Name = "Mail", Instruction = "Als E-Mail" });
        return s;
    }

    [Theory]
    [InlineData("Prompt: ich brauche eine Funktion.", "prompt", "Ich brauche eine Funktion.")]
    [InlineData("prompt, schreib mir ein Skript", "prompt", "Schreib mir ein Skript")]
    [InlineData("Englisch. Wir sehen uns morgen.", "translate", "Wir sehen uns morgen.")]
    [InlineData("Auf Englisch: Danke für alles.", "translate", "Danke für alles.")]
    [InlineData("Emoji, das ist super", "emoji", "Das ist super")]
    [InlineData("Mail: Hallo Anna, kurze Frage", "mail", "Hallo Anna, kurze Frage")]   // eigener Modus per Name
    [InlineData("  „Ruhig“ – Das nervt total.", "calm", "Das nervt total.")]
    public void DetectsKeywords(string text, string modeId, string rest)
    {
        var m = ModeKeywords.Detect(text, S);
        Assert.NotNull(m);
        Assert.Equal(modeId, m!.Mode.Id);
        Assert.Equal(rest, m.Rest);
        Assert.False(m.IsNote);
    }

    [Theory]
    [InlineData("Englisch ist eine schwierige Sprache.")]   // kein Satzzeichen → kein Befehl
    [InlineData("Wir brauchen einen Prompt: der fehlt noch.")] // nicht am Anfang
    [InlineData("Promptly answered.")]
    [InlineData("")]
    public void IgnoresNormalSentences(string text) => Assert.Null(ModeKeywords.Detect(text, S));

    [Fact]
    public void NoteKeywordFilesAsNote()
    {
        var m = ModeKeywords.Detect("Notiz: Milch kaufen", S)!;
        Assert.True(m.IsNote);
        Assert.Equal("Milch kaufen", m.Rest);
        Assert.Equal(S.Notes.ModeId, m.Mode.Id);
    }
}

public class PromptModeTests
{
    private sealed class StubTranscriber(string text) : ITranscriber
    {
        public Task<TranscriptionResult> TranscribeAsync(TranscriptionRequest request, CancellationToken ct) =>
            Task.FromResult(new TranscriptionResult(text, "de"));
    }

    private sealed class RecordingChat(string answer) : IChatClient
    {
        public string? System;
        public string? User;
        public Task<string> CompleteAsync(string systemPrompt, string userMessage, double temperature, CancellationToken ct)
        {
            System = systemPrompt;
            User = userMessage;
            return Task.FromResult(answer);
        }
    }

    [Fact]
    public void PromptModeIsBuiltInWithoutHotkey()
    {
        var p = new AppSettings().FindMode(BuiltInModes.PromptId)!;
        Assert.Equal(ModeKind.Prompt, p.Kind);
        Assert.True(p.Hotkey.IsEmpty);
        Assert.Contains("NICHT", PromptBuilder.BuildSystemPrompt(p, "de", []));
    }

    [Fact]
    public async Task KeywordSwitchesModeAndStripsKeyword()
    {
        var chat = new RecordingChat("Schreibe eine Python-Funktion, die CSV-Dateien einliest.");
        var r = await new DictationPipeline(new StubTranscriber("Prompt: ähm schreib mir ne Funktion die CSV einliest"), chat)
            .RunAsync("a.wav", new AppSettings().DefaultMode, new AppSettings(), default, allowKeywords: true);
        Assert.Equal(BuiltInModes.PromptId, r.Mode.Id);
        Assert.Equal("Prompt", r.Keyword);
        Assert.DoesNotContain("Prompt:", chat.User);
        Assert.Contains("Forme daraus einen klaren", chat.System);
        Assert.Equal("Schreibe eine Python-Funktion, die CSV-Dateien einliest.", r.Final);
    }

    [Fact]
    public async Task KeywordsIgnoredWhenNotAllowedOrDisabled()
    {
        var chat = new RecordingChat("x");
        var s = new AppSettings();
        var r = await new DictationPipeline(new StubTranscriber("Prompt: Test"), chat).RunAsync("a.wav", s.DefaultMode, s, default);
        Assert.Equal("Prompt: Test", r.Final);

        s.VoiceKeywords = false;
        r = await new DictationPipeline(new StubTranscriber("Prompt: Test"), chat).RunAsync("a.wav", s.DefaultMode, s, default, allowKeywords: true);
        Assert.Equal("Prompt: Test", r.Final);
    }

    [Fact]
    public async Task NoteKeywordMarksResult()
    {
        var r = await new DictationPipeline(new StubTranscriber("Notiz. Milch kaufen"), null)
            .RunAsync("a.wav", new AppSettings().DefaultMode, new AppSettings(), default, allowKeywords: true);
        Assert.True(r.SaveAsNote);
        Assert.Equal("Milch kaufen", r.Final);
    }

    [Fact]
    public void LongerStructuredPromptIsAccepted() =>
        Assert.Equal(RewriteVerdict.Ok, RewriteGuard.Check("kurz", new string('x', 500), ModeKind.Prompt));

    [Theory]
    [InlineData("chrome", "Neuer Chat - ChatGPT - Google Chrome", "prompt")]
    [InlineData("msedge", "Claude - Microsoft Edge", "prompt")]
    [InlineData("Cursor", "main.py - SonicScript - Cursor", "prompt")]
    [InlineData("notepad", "Unbenannt - Editor", "verbatim")]
    public void DefaultAppRulesUsePromptModeInAiApps(string proc, string title, string expected) =>
        Assert.Equal(expected, AppRuleResolver.ResolveModeId(new AppSettings(), new ForegroundApp(proc, title)));

    [Fact]
    public void Schema3UsersGetPromptRulesKeywordsAndFewerKeys()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, """
        { "schemaVersion": 3,
          "notes": { "hotkey": "Ctrl+Shift+-" },
          "appRules": [ { "processName": "outlook", "modeId": "improve" } ],
          "modes": [
            { "id": "verbatim", "kind": "verbatim", "name": "A" },
            { "id": "improve", "kind": "improve", "name": "B", "hotkey": "Ctrl+." },
            { "id": "calm", "kind": "calm", "name": "C", "hotkey": "Ctrl+Shift+." },
            { "id": "emoji", "kind": "emoji", "name": "D", "hotkey": "F7" },
            { "id": "translate", "kind": "translate", "name": "E", "hotkey": "Ctrl+-" } ] }
        """);
        var s = new SettingsStore(path).Load();
        Assert.True(s.FindMode("calm")!.Hotkey.IsEmpty);
        Assert.Equal("F7", s.FindMode("emoji")!.Hotkey.ToString());      // selbst gewählt → bleibt
        Assert.True(s.FindMode("translate")!.Hotkey.IsEmpty);
        Assert.Equal("Ctrl+.", s.FindMode("improve")!.Hotkey.ToString());
        Assert.True(s.Notes.Hotkey.IsEmpty);
        Assert.Contains("Englisch", s.FindMode("translate")!.Keywords);
        Assert.NotNull(s.FindMode("prompt"));
        Assert.Equal("outlook", s.AppRules[0].ProcessName);              // eigene Regel bleibt vorne
        Assert.Contains(s.AppRules, r => r.TitleContains == "ChatGPT" && r.ModeId == "prompt");
    }
}
