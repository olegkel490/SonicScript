using SonicScript.Core.LanguageModel;
using SonicScript.Core.Modes;
using SonicScript.Core.Net;
using SonicScript.Core.Pipeline;
using SonicScript.Core.Settings;
using SonicScript.Core.Transcription;

namespace SonicScript.Core.Tests;

public class FallbackTests
{
    private sealed class ScriptedChat(params Func<string>[] steps) : IChatClient
    {
        public int Calls;
        public Task<string> CompleteAsync(string systemPrompt, string userMessage, double temperature, CancellationToken ct)
        {
            var step = steps[Math.Min(Calls++, steps.Length - 1)];
            return Task.FromResult(step());
        }
    }

    private sealed class Transcriber(string? text, Exception? fail = null) : ITranscriber
    {
        public int Calls;
        public Task<TranscriptionResult> TranscribeAsync(TranscriptionRequest request, CancellationToken ct)
        {
            Calls++;
            return fail is null ? Task.FromResult(new TranscriptionResult(text!, "de")) : Task.FromException<TranscriptionResult>(fail);
        }
    }

    private static readonly AppSettings S = new() { TrimSilence = false };
    private static ModeDefinition Improve => S.FindMode(BuiltInModes.ImproveId)!;
    private static Task<DictationResult> Run(IChatClient chat, IChatClient? fallback = null) =>
        new DictationPipeline(new Transcriber("x"), chat, fallback).RunTranscriptAsync(new TranscriptionResult("das ist ein test", "de"), Improve, S, default);

    [Fact]
    public async Task RetriesOnceOnTransientError()
    {
        var chat = new ScriptedChat(() => throw new RemoteServiceException("timeout"), () => "Das ist ein Test.");
        var r = await Run(chat);
        Assert.Equal("Das ist ein Test.", r.Final);
        Assert.Equal(2, chat.Calls);
        Assert.False(r.UsedFallbackModel);
    }

    [Fact]
    public async Task UsesFallbackModelWhenMainKeepsFailing()
    {
        var chat = new ScriptedChat(() => throw new RemoteServiceException("http_503: down"));
        var fallback = new ScriptedChat(() => "Das ist ein Test.");
        var r = await Run(chat, fallback);
        Assert.Equal("Das ist ein Test.", r.Final);
        Assert.True(r.UsedFallbackModel);
        Assert.Equal(DictationWarning.None, r.Warning);
    }

    [Fact]
    public async Task PermanentErrorIsNotRetriedButFallbackIsAsked()
    {
        var chat = new ScriptedChat(() => throw new RemoteServiceException("http_401: bad key"));
        var r = await Run(chat);
        Assert.Equal(1, chat.Calls);
        Assert.Equal(DictationWarning.ModeFailed, r.Warning);
        Assert.Equal("das ist ein test", r.Final);
    }

    [Fact]
    public async Task RejectedAnswerIsOfferedAndFallbackTried()
    {
        var tooLong = new string('x', 2000);
        var chat = new ScriptedChat(() => tooLong);
        var fallback = new ScriptedChat(() => tooLong);
        var r = await Run(chat, fallback);
        Assert.Equal(DictationWarning.ModeRejected, r.Warning);
        Assert.Equal("TooLong", r.WarningDetail);
        Assert.Equal(tooLong, r.RejectedText);
        Assert.Equal(1, fallback.Calls);
        Assert.Equal("das ist ein test", r.Final);
    }

    [Fact]
    public async Task EmptyAnswerIsRetried()
    {
        var chat = new ScriptedChat(() => "", () => "Das ist ein Test.");
        var r = await Run(chat);
        Assert.Equal("Das ist ein Test.", r.Final);
    }

    [Fact]
    public async Task LocalTranscriberStepsInWhenCloudFails()
    {
        using var dir = new TempDir();
        var wav = Path.Combine(dir.Path, "a.wav");
        Audio.PcmWav.Write(wav, new short[16000], 16000);
        var cloud = new Transcriber(null, new RemoteServiceException("unreachable"));
        var local = new Transcriber("hallo welt");
        var r = await new DictationPipeline(cloud, null, null, local).RunAsync(wav, S.DefaultMode, S, default);
        Assert.Equal("hallo welt", r.Final);
        Assert.True(r.UsedFallbackTranscriber);
    }

    [Fact]
    public async Task BothTranscribersFailingKeepsRecording()
    {
        using var dir = new TempDir();
        var wav = Path.Combine(dir.Path, "a.wav");
        Audio.PcmWav.Write(wav, new short[16000], 16000);
        var cloud = new Transcriber(null, new RemoteServiceException("unreachable"));
        var local = new Transcriber(null, new InvalidOperationException("no model"));
        var ex = await Assert.ThrowsAsync<TranscriptionFailedException>(() => new DictationPipeline(cloud, null, null, local).RunAsync(wav, S.DefaultMode, S, default));
        Assert.StartsWith("unreachable", ex.Message);
    }

    [Fact]
    public async Task RewriteTextReprocessesHistoryText()
    {
        var chat = new ScriptedChat(() => "Hi Anna,\n\nI will come.\n\nBest regards");
        var r = await new DictationPipeline(new Transcriber("x"), chat).RewriteTextAsync("ich komme", "de", S.FindMode(BuiltInModes.MailEnglishId)!, S, default);
        Assert.StartsWith("Hi Anna,", r.Final);
        Assert.Equal("ich komme", r.Verbatim);
    }

    [Theory]
    [InlineData("timeout", true)]
    [InlineData("http_503: x", true)]
    [InlineData("http_429: slow down", true)]
    [InlineData("unreachable: dns", true)]
    [InlineData("http_401: key", false)]
    [InlineData("http_402: credit", false)]
    public void TransientErrors(string error, bool expected) => Assert.Equal(expected, DictationPipeline.IsTransient(error));
}
