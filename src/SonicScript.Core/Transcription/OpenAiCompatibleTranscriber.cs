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

    /// <summary>OpenRouter erwartet JSON mit Base64-Audio (<c>input_audio</c>), nicht OpenAIs Multipart-Upload.</summary>
    public bool UsesJsonBody => IsOpenRouter(_baseUrl);

    internal static bool IsOpenRouter(string baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var u) &&
        (u.Host.Equals("openrouter.ai", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".openrouter.ai", StringComparison.OrdinalIgnoreCase));

    public async Task<TranscriptionResult> TranscribeAsync(TranscriptionRequest request, CancellationToken ct)
    {
        var audio = await File.ReadAllBytesAsync(request.AudioPath, ct).ConfigureAwait(false);
        using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/audio/transcriptions")
        {
            Content = UsesJsonBody ? BuildJsonBody(audio, _model, request) : BuildMultipartBody(audio, _model, request),
        };
        Http.ApplyCommonHeaders(req, _apiKey);
        var body = await Http.SendAsync(_http, req, Timeout, ct).ConfigureAwait(false);
        return Parse(body);
    }

    /// <summary>OpenRouter: <c>{"model": …, "input_audio": {"data": base64, "format": "wav"}, "language": …}</c>.</summary>
    internal static HttpContent BuildJsonBody(byte[] audio, string model, TranscriptionRequest request)
    {
        var payload = new Dictionary<string, object>
        {
            ["model"] = model,
            ["input_audio"] = new Dictionary<string, string> { ["data"] = Convert.ToBase64String(audio), ["format"] = "wav" },
        };
        if (!string.IsNullOrWhiteSpace(request.Language) && request.Language != "auto") payload["language"] = request.Language;
        return new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
    }

    /// <summary>
    /// OpenAI-Stil-Multipart, mit konservativen Headern: Feldnamen in Anführungszeichen, kein <c>filename*</c>,
    /// Boundary ohne Anführungszeichen – manche Server-Parser lehnen die .NET-Standardform sonst ab.
    /// </summary>
    internal static HttpContent BuildMultipartBody(byte[] audio, string model, TranscriptionRequest request)
    {
        var boundary = "----SonicScript" + Guid.NewGuid().ToString("N");
        var form = new MultipartFormDataContent(boundary);
        form.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=" + boundary);

        var file = new ByteArrayContent(audio);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"file\"", FileName = "\"audio.wav\"" };
        form.Add(file);

        void Field(string name, string value)
        {
            var c = new StringContent(value);
            c.Headers.ContentType = null;
            c.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = $"\"{name}\"" };
            form.Add(c);
        }
        Field("model", model);
        Field("response_format", "json");
        if (!string.IsNullOrWhiteSpace(request.Prompt)) Field("prompt", request.Prompt);
        if (!string.IsNullOrWhiteSpace(request.Language) && request.Language != "auto") Field("language", request.Language);
        return form;
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
