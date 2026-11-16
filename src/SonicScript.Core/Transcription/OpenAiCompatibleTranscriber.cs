using System.Net.Http.Headers;
using System.Text.Json;
using SonicScript.Core.Net;

namespace SonicScript.Core.Transcription;

/// <summary>
/// Cloud-Transkription über <c>POST {base}/audio/transcriptions</c> (Multipart). Funktioniert mit OpenRouter
/// (Modell z. B. "openai/whisper-large-v3"), OpenAI, Groq und eigenen OpenAI-kompatiblen Servern.
/// </summary>
public sealed class OpenAiCompatibleTranscriber : ITranscriber
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly string? _apiKey;

    public OpenAiCompatibleTranscriber(HttpClient http, string baseUrl, string model, string? apiKey)
    {
        if (!EndpointPolicy.TryValidate(baseUrl, !string.IsNullOrEmpty(apiKey), out var err))
            throw new RemoteServiceException(err!);
        if (string.IsNullOrWhiteSpace(model)) throw new RemoteServiceException("no_model");
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model.Trim();
        _apiKey = string.IsNullOrEmpty(apiKey) ? null : apiKey;
    }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(90);

    public async Task<TranscriptionResult> TranscribeAsync(TranscriptionRequest request, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        await using var audio = new FileStream(request.AudioPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        var file = new StreamContent(audio);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "audio.wav");
        form.Add(new StringContent(_model), "model");
        form.Add(new StringContent("json"), "response_format");
        if (!string.IsNullOrWhiteSpace(request.Prompt)) form.Add(new StringContent(request.Prompt), "prompt");
        if (!string.IsNullOrWhiteSpace(request.Language) && request.Language != "auto")
            form.Add(new StringContent(request.Language), "language");

        using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/audio/transcriptions") { Content = form };
        Http.ApplyCommonHeaders(req, _apiKey);
        var body = await Http.SendAsync(_http, req, Timeout, ct).ConfigureAwait(false);
        return Parse(body);
    }

    internal static TranscriptionResult Parse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var text = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            var lang = root.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null;
            return new TranscriptionResult(text.Trim(), NormalizeLanguage(lang));
        }
        catch (JsonException)
        {
            // manche Server liefern trotz json reinen Text
            return new TranscriptionResult(body.Trim(), null);
        }
    }

    /// <summary>OpenAI liefert teils "german" statt "de".</summary>
    public static string? NormalizeLanguage(string? lang) => lang?.ToLowerInvariant() switch
    {
        null or "" => null,
        "german" or "deutsch" => "de",
        "english" => "en",
        "french" => "fr",
        "spanish" => "es",
        "italian" => "it",
        var s when s.Length > 2 && s[2] == '-' => s[..2],
        var s => s,
    };
}
