using SonicScript.Core.LanguageModel;
using SonicScript.Core.Modes;
using SonicScript.Core.Net;
using SonicScript.Core.Pipeline;
using SonicScript.Core.Settings;
using SonicScript.Core.Transcription;

namespace SonicScript.Core.Tests;

public class PipelineTests
{
    private sealed class StubTranscriber(string text, string? lang = "de", Exception? fail = null) : ITranscriber
    {
        public TranscriptionRequest? LastRequest;
        public Task<TranscriptionResult> TranscribeAsync(TranscriptionRequest request, CancellationToken ct)
        {
            LastRequest = request;
            return fail is null ? Task.FromResult(new TranscriptionResult(text, lang)) : Task.FromException<TranscriptionResult>(fail);
        }
    }

    private sealed class StubChat(Func<string, string, string> answer) : IChatClient
    {
        public int Calls;
        public Task<string> CompleteAsync(string systemPrompt, string userMessage, double temperature, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(answer(systemPrompt, userMessage));
        }
    }

    private static readonly AppSettings Settings = new()
    {
        Dictionary = new DictionarySettings { Terms = ["SonicScript"], Replacements = [new Replacement { From = "n acht n", To = "n8n" }] },
    };

    private static ModeDefinition Mode(string id) => Settings.FindMode(id)!;

    [Fact]
    public async Task Verbatim_PostProcessesWithoutLanguageModel()
    {
        var chat = new StubChat((_, _) => throw new Exception("darf nicht aufgerufen werden"));
        var t = new StubTranscriber("Treffen um acht Uhr in n acht n mit sonicscript.");
        var r = await new DictationPipeline(t, chat).RunAsync("a.wav", Mode(BuiltInModes.VerbatimId), Settings, default);
        Assert.Equal("Treffen um 8 Uhr in n8n mit SonicScript.", r.Final);
        Assert.Equal(0, chat.Calls);
        Assert.Equal("SonicScript", t.LastRequest?.Prompt);
    }

    [Fact]
    public async Task Improve_UsesCleanedModelOutput()
    {
        var chat = new StubChat((_, user) => "Hier ist der Text:\n\nHallo, wie geht es dir?");
        var r = await new DictationPipeline(new StubTranscriber("ähm hallo wie geht es dir"), chat)
            .RunAsync("a.wav", Mode(BuiltInModes.ImproveId), Settings, default);
        Assert.Equal("Hallo, wie geht es dir?", r.Final);
        Assert.Equal("ähm hallo wie geht es dir", r.Verbatim);
        Assert.Equal(DictationWarning.None, r.Warning);
    }

    [Fact]
    public async Task ModelFailure_FallsBackToVerbatim()
    {
        var chat = new StubChat((_, _) => throw new RemoteServiceException("unreachable"));
        var r = await new DictationPipeline(new StubTranscriber("hallo welt"), chat)
            .RunAsync("a.wav", Mode(BuiltInModes.EmojiId), Settings, default);
        Assert.Equal("hallo welt", r.Final);
        Assert.Equal(DictationWarning.ModeFailed, r.Warning);
    }

    [Fact]
    public async Task RejectedOutput_FallsBackToVerbatim()
    {
        var chat = new StubChat((_, _) => "");
        var r = await new DictationPipeline(new StubTranscriber("hallo welt"), chat)
            .RunAsync("a.wav", Mode(BuiltInModes.ImproveId), Settings, default);
        Assert.Equal(DictationWarning.ModeRejected, r.Warning);
        Assert.Equal("hallo welt", r.Final);
    }

    [Fact]
    public async Task NoChatConfigured_WarnsAndKeepsVerbatim()
    {
        var r = await new DictationPipeline(new StubTranscriber("hallo"), null)
            .RunAsync("a.wav", Mode(BuiltInModes.CalmId), Settings, default);
        Assert.Equal(DictationWarning.ModeNotConfigured, r.Warning);
        Assert.Equal("hallo", r.Final);
    }

    [Fact]
    public async Task Hallucination_YieldsEmptyResultWithoutModelCall()
    {
        var chat = new StubChat((_, _) => "x");
        var r = await new DictationPipeline(new StubTranscriber("Untertitel im Auftrag des ZDF, 2019"), chat)
            .RunAsync("a.wav", Mode(BuiltInModes.ImproveId), Settings, default);
        Assert.True(r.IsEmpty);
        Assert.Equal(0, chat.Calls);
    }

    [Fact]
    public async Task TranscriptionError_IsWrapped()
    {
        var t = new StubTranscriber("", fail: new RemoteServiceException("http_500"));
        await Assert.ThrowsAsync<TranscriptionFailedException>(() =>
            new DictationPipeline(t, null).RunAsync("a.wav", Mode(BuiltInModes.VerbatimId), Settings, default));
    }

    [Fact]
    public async Task EnglishDictation_SkipsGermanTimeNormalization()
    {
        var r = await new DictationPipeline(new StubTranscriber("acht Uhr", "en"), null)
            .RunAsync("a.wav", Mode(BuiltInModes.VerbatimId), Settings, default);
        Assert.Equal("acht Uhr", r.Final);
    }

    [Fact]
    public void WhisperPromptIsCapped()
    {
        var terms = Enumerable.Range(0, 100).Select(i => "Begriff" + i).ToList();
        Assert.True(DictationPipeline.BuildWhisperPrompt(terms)!.Length <= 200);
        Assert.Null(DictationPipeline.BuildWhisperPrompt([" "]));
    }
}
