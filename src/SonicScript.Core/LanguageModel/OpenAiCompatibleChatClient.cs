using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SonicScript.Core.Net;

namespace SonicScript.Core.LanguageModel;

public interface IChatClient
{
    Task<string> CompleteAsync(string systemPrompt, string userMessage, double temperature, CancellationToken ct);
}

/// <summary>
/// Chat-Completions gegen jede OpenAI-kompatible API: Ollama, LM Studio, OpenRouter, OpenAI, eigener Server.
/// </summary>
public sealed class OpenAiCompatibleChatClient : IChatClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly string? _apiKey;

    public OpenAiCompatibleChatClient(HttpClient http, string baseUrl, string model, string? apiKey)
    {
        if (!EndpointPolicy.TryValidate(baseUrl, !string.IsNullOrEmpty(apiKey), out var err))
            throw new RemoteServiceException(err!);
        if (string.IsNullOrWhiteSpace(model)) throw new RemoteServiceException("no_model");
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model.Trim();
        _apiKey = string.IsNullOrEmpty(apiKey) ? null : apiKey;
    }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Verfügbare Modelle über <c>GET {base}/models</c> (Ollama, LM Studio, OpenRouter, OpenAI).</summary>
    public static async Task<IReadOnlyList<string>> ListModelsAsync(HttpClient http, string baseUrl, string? apiKey, CancellationToken ct)
    {
        if (!EndpointPolicy.TryValidate(baseUrl, !string.IsNullOrEmpty(apiKey), out var err))
            throw new RemoteServiceException(err!);
        using var req = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + "/models");
        Http.ApplyCommonHeaders(req, string.IsNullOrEmpty(apiKey) ? null : apiKey);
        var body = await Http.SendAsync(http, req, TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
        return ParseModelList(body);
    }

    internal static IReadOnlyList<string> ParseModelList(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return [];
            return data.EnumerateArray()
                .Select(e => e.TryGetProperty("id", out var id) ? id.GetString() : null)
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Großzügig, weil "denkende" Modelle (Reasoning) ihre Überlegungen auf dieses Limit anrechnen – zu knapp, und die
    /// eigentliche Antwort bleibt leer. Bezahlt werden nur tatsächlich erzeugte Tokens.
    /// </summary>
    public static int MaxTokensFor(string text) => Math.Clamp(text.Length / 2 + 1024, 2048, 16384);

    public async Task<string> CompleteAsync(string systemPrompt, string userMessage, double temperature, CancellationToken ct)
    {
        var maxTokens = MaxTokensFor(userMessage);
        var (content, finish) = await SendAsync(systemPrompt, userMessage, temperature, maxTokens, ct).ConfigureAwait(false);
        // Leer, weil das Limit von den Überlegungen des Modells aufgebraucht wurde → einmal mit viel mehr Platz.
        if (string.IsNullOrWhiteSpace(content) && finish == "length")
            (content, finish) = await SendAsync(systemPrompt, userMessage, temperature, Math.Min(maxTokens * 4, 32768), ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content) && finish == "length")
            throw new RemoteServiceException("output_limit: das Modell hat sein Token-Limit nur für Überlegungen verbraucht");
        return content;
    }

    private async Task<(string Content, string? Finish)> SendAsync(string systemPrompt, string userMessage, double temperature, int maxTokens, CancellationToken ct)
    {
        var payload = new
        {
            model = _model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage },
            },
            temperature,
            max_tokens = maxTokens,
            stream = false,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/chat/completions")
        {
            Content = JsonContent.Create(payload),
        };
        Http.ApplyCommonHeaders(req, _apiKey);

        var body = await Http.SendAsync(_http, req, Timeout, ct).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(body);
            var choice = doc.RootElement.GetProperty("choices")[0];
            var finish = choice.TryGetProperty("finish_reason", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
            var msg = choice.GetProperty("message");
            var content = msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : "";
            return (content, finish);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new RemoteServiceException("bad_response", ex);
        }
    }
}

