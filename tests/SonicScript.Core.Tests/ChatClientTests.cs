using System.Net;
using SonicScript.Core.LanguageModel;
using SonicScript.Core.Modes;
using SonicScript.Core.Net;

namespace SonicScript.Core.Tests;

public class ChatClientLimitTests
{
    private sealed class QueueHandler(params string[] responses) : HttpMessageHandler
    {
        private int _i;
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responses[Math.Min(_i++, responses.Length - 1)]) };
        }
    }

    private static string Answer(string? content, string finish) =>
        "{\"choices\":[{\"finish_reason\":\"" + finish + "\",\"message\":{\"role\":\"assistant\",\"content\":" +
        (content is null ? "null" : "\"" + content + "\"") + "}}]}";

    private static OpenAiCompatibleChatClient Client(QueueHandler h) =>
        new(new HttpClient(h), "https://openrouter.ai/api/v1", "some/model", "key");

    [Fact]
    public async Task RetriesWithMoreTokensWhenReasoningUsedUpTheLimit()
    {
        var h = new QueueHandler(Answer("", "length"), Answer("Hi Anna,", "stop"));
        Assert.Equal("Hi Anna,", await Client(h).CompleteAsync("s", "u", 0.3, default));
        Assert.Equal(2, h.Bodies.Count);
        Assert.Contains("\"max_tokens\":2048", h.Bodies[0]);
        Assert.Contains("\"max_tokens\":8192", h.Bodies[1]);
    }

    [Fact]
    public async Task ReportsTokenLimitInsteadOfEmptyText()
    {
        var h = new QueueHandler(Answer(null, "length"));
        var ex = await Assert.ThrowsAsync<RemoteServiceException>(() => Client(h).CompleteAsync("s", "u", 0.3, default));
        Assert.StartsWith("output_limit", ex.Message);
    }

    [Fact]
    public async Task NormalAnswerNeedsOneCall()
    {
        var h = new QueueHandler(Answer("Text", "stop"));
        Assert.Equal("Text", await Client(h).CompleteAsync("s", "u", 0.3, default));
        Assert.Single(h.Bodies);
    }

    [Fact]
    public void MailFromShortDictationIsNotTooLong()
    {
        var input = "schreib Anna dass ich morgen komme";
        var mail = "Hi Anna,\n\n" + string.Join(" ", Enumerable.Repeat("I wanted to let you know that I will come by tomorrow.", 8)) + "\n\nBest regards";
        Assert.Equal(RewriteVerdict.Ok, RewriteGuard.Check(input, mail, ModeKind.MailEnglish));
        Assert.Equal(RewriteVerdict.Ok, RewriteGuard.Check(input, mail, ModeKind.Mail));
        Assert.Equal(RewriteVerdict.TooLong, RewriteGuard.Check(input, mail, ModeKind.Improve));
    }
}
