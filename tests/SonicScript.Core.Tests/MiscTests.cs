using SonicScript.Core.Net;
using SonicScript.Core.Rules;
using SonicScript.Core.Settings;
using SonicScript.Core.Transcription;
using SonicScript.Core.Updates;

namespace SonicScript.Core.Tests;

public class EndpointPolicyTests
{
    [Theory]
    [InlineData("https://openrouter.ai/api/v1", true, true)]
    [InlineData("http://localhost:11434/v1", true, true)]
    [InlineData("http://127.0.0.1:1234/v1", true, true)]
    [InlineData("http://192.168.1.20:8080/v1", true, true)]
    [InlineData("http://100.101.102.103/v1", true, true)]   // Tailscale
    [InlineData("http://box.tail1234.ts.net/v1", true, true)]
    [InlineData("http://example.com/v1", true, false)]      // Schlüssel im Klartext ins Internet
    [InlineData("http://example.com/v1", false, true)]      // ohne Schlüssel ok
    [InlineData("ftp://example.com", false, false)]
    [InlineData("kein url", false, false)]
    public void Validates(string url, bool hasSecret, bool ok) =>
        Assert.Equal(ok, EndpointPolicy.TryValidate(url, hasSecret, out _));
}

public class AppRuleTests
{
    private static readonly AppSettings S = new()
    {
        AppRules =
        [
            new AppRule { ProcessName = "chrome.exe", TitleContains = "Gmail", ModeId = "improve" },
            new AppRule { ProcessName = "slack", ModeId = "emoji" },
            new AppRule { ProcessName = "WindowsTerminal", ModeId = "verbatim", Insertion = InsertionMethod.PasteShift },
            new AppRule { ProcessName = "foo", ModeId = "gibt-es-nicht" },
        ],
    };

    [Theory]
    [InlineData("chrome", "Posteingang - Gmail - Google Chrome", "improve")]
    [InlineData("chrome", "YouTube - Google Chrome", "verbatim")]
    [InlineData("Slack.exe", "Slack", "emoji")]
    [InlineData("foo", "", "verbatim")]   // unbekannter Modus → Standard
    public void ResolvesMode(string proc, string title, string expected) =>
        Assert.Equal(expected, AppRuleResolver.ResolveModeId(S, new ForegroundApp(proc, title)));

    [Fact]
    public void ResolvesInsertion()
    {
        Assert.Equal(InsertionMethod.PasteShift, AppRuleResolver.ResolveInsertion(S, new ForegroundApp("windowsterminal", "pwsh")));
        Assert.Equal(InsertionMethod.Paste, AppRuleResolver.ResolveInsertion(S, new ForegroundApp("slack", "")));
        Assert.Equal(InsertionMethod.Paste, AppRuleResolver.ResolveInsertion(S, null));
    }
}

public class TranscriptionMiscTests
{
    [Fact]
    public void RecommendsByHardwareAndBenchmark()
    {
        Assert.Equal("large-v3-turbo-q5_0", ModelRecommender.RecommendInitial(new HardwareInfo(true, 4)));
        Assert.Equal("small", ModelRecommender.RecommendInitial(new HardwareInfo(false, 8)));
        Assert.Equal("small-q5_1", ModelRecommender.RecommendInitial(new HardwareInfo(false, 4)));

        Assert.Equal("large-v3-turbo-q5_0", ModelRecommender.RecommendAfterBenchmark("large-v3-turbo-q5_0", 0.2));
        Assert.Equal("medium-q5_0", ModelRecommender.RecommendAfterBenchmark("large-v3-turbo-q5_0", 0.5));
        Assert.Equal("small", ModelRecommender.RecommendAfterBenchmark("large-v3-turbo-q5_0", 2.0));
        Assert.Equal("base", ModelRecommender.RecommendAfterBenchmark("base", 5));
    }

    [Fact]
    public void CatalogAndMagic()
    {
        Assert.All(WhisperModelCatalog.All, m => Assert.StartsWith("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-", m.Url));
        Assert.True(WhisperModelCatalog.HasGgmlMagic([0x6c, 0x6d, 0x67, 0x67, 0]));
        Assert.False(WhisperModelCatalog.HasGgmlMagic("<!DOCTYPE"u8));
    }

    [Theory]
    [InlineData("{\"text\":\" Hallo \",\"language\":\"german\"}", "Hallo", "de")]
    [InlineData("{\"text\":\"Hi\"}", "Hi", null)]
    [InlineData("plain text", "plain text", null)]
    public void ParsesCloudResponses(string body, string text, string? lang)
    {
        var r = OpenAiCompatibleTranscriber.Parse(body);
        Assert.Equal(text, r.Text);
        Assert.Equal(lang, r.Language);
    }
}

public class UpdateCheckerTests
{
    [Fact]
    public void ParsesRelease()
    {
        const string json = """
        {"tag_name":"v1.2.0","html_url":"https://github.com/x/y/releases/tag/v1.2.0","draft":false,"prerelease":false,
         "assets":[{"name":"SonicScript.exe","browser_download_url":"https://example/SonicScript.exe"}]}
        """;
        var r = UpdateChecker.Parse(json)!;
        Assert.Equal(new Version(1, 2, 0), r.Version);
        Assert.Equal("https://example/SonicScript.exe", r.DownloadUrl);
    }

    [Theory]
    [InlineData("v0.3.1", 0, 3, 1)]
    [InlineData("1.4", 1, 4, 0)]
    [InlineData("v2.0.0-beta.1", 2, 0, 0)]
    public void ParsesTags(string tag, int a, int b, int c)
    {
        Assert.True(UpdateChecker.TryParseVersion(tag, out var v));
        Assert.Equal(new Version(a, b, c), v);
    }

    [Fact]
    public void IgnoresPrereleases() =>
        Assert.Null(UpdateChecker.Parse("""{"tag_name":"v9.0.0","html_url":"","prerelease":true}"""));
}

public class WavFileTests
{
    [Fact]
    public void WritesAndMeasures()
    {
        using var dir = new TempDir();
        var p = Path.Combine(dir.Path, "n.wav");
        SonicScript.Core.Audio.WavFile.WriteNoise(p, TimeSpan.FromSeconds(2.5));
        Assert.Equal(2.5, SonicScript.Core.Audio.WavFile.GetDuration(p)!.Value.TotalSeconds, 3);
        File.WriteAllText(p, "kein wav");
        Assert.Null(SonicScript.Core.Audio.WavFile.GetDuration(p));
    }
}

public class ChatClientTests
{
    [Fact]
    public void ParsesModelList() =>
        Assert.Equal(["gemma3:4b", "qwen3:4b"],
            SonicScript.Core.LanguageModel.OpenAiCompatibleChatClient.ParseModelList("""{"object":"list","data":[{"id":"qwen3:4b"},{"id":"gemma3:4b"}]}"""));

    [Fact]
    public void RejectsKeyOverPlainHttp() =>
        Assert.Throws<RemoteServiceException>(() =>
            new SonicScript.Core.LanguageModel.OpenAiCompatibleChatClient(new HttpClient(), "http://example.com/v1", "m", "secret"));
}

public class CloudTranscriptionRequestTests
{
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? ContentType;
        public string? Body;
        public Uri? Uri;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Uri = request.RequestUri;
            ContentType = request.Content?.Headers.ContentType?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"text\":\"Hallo Welt\"}") };
        }
    }

    private static string Wav()
    {
        var p = Path.Combine(Path.GetTempPath(), $"ss-{Guid.NewGuid():N}.wav");
        SonicScript.Core.Audio.WavFile.WriteNoise(p, TimeSpan.FromMilliseconds(200));
        return p;
    }

    [Fact]
    public async Task OpenRouterGetsJsonWithBase64Audio()
    {
        var h = new CaptureHandler();
        var t = new OpenAiCompatibleTranscriber(new HttpClient(h), "https://openrouter.ai/api/v1", "openai/whisper-large-v3", "key");
        var wav = Wav();
        var r = await t.TranscribeAsync(new TranscriptionRequest(wav, "de", "SonicScript"), default);
        File.Delete(wav);

        Assert.Equal("Hallo Welt", r.Text);
        Assert.Equal("https://openrouter.ai/api/v1/audio/transcriptions", h.Uri!.ToString());
        Assert.StartsWith("application/json", h.ContentType);
        using var doc = System.Text.Json.JsonDocument.Parse(h.Body!);
        Assert.Equal("openai/whisper-large-v3", doc.RootElement.GetProperty("model").GetString());
        Assert.Equal("wav", doc.RootElement.GetProperty("input_audio").GetProperty("format").GetString());
        var data = Convert.FromBase64String(doc.RootElement.GetProperty("input_audio").GetProperty("data").GetString()!);
        Assert.Equal("RIFF"u8.ToArray(), data[..4]);
        Assert.Equal("de", doc.RootElement.GetProperty("language").GetString());
    }

    [Fact]
    public async Task OthersGetCleanMultipart()
    {
        var h = new CaptureHandler();
        var t = new OpenAiCompatibleTranscriber(new HttpClient(h), "https://api.openai.com/v1", "whisper-1", "key");
        var wav = Wav();
        await t.TranscribeAsync(new TranscriptionRequest(wav, null, null), default);
        File.Delete(wav);

        Assert.StartsWith("multipart/form-data; boundary=----SonicScript", h.ContentType);
        Assert.DoesNotContain("boundary=\"", h.ContentType);
        Assert.Contains("Content-Disposition: form-data; name=\"file\"; filename=\"audio.wav\"", h.Body);
        Assert.Contains("name=\"model\"", h.Body);
        Assert.DoesNotContain("filename*", h.Body);
        Assert.DoesNotContain("name=\"language\"", h.Body);
    }
}
