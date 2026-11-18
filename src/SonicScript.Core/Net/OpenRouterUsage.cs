using System.Text.Json;

namespace SonicScript.Core.Net;

/// <summary>Verbrauch eines OpenRouter-Schlüssels in US-Dollar (Credits). null = nicht geliefert.</summary>
public sealed record OpenRouterKeyUsage(double Total, double? Day, double? Week, double? Month, double? Limit, double? Remaining);

/// <summary>Fragt den Verbrauch eines OpenRouter-API-Schlüssels ab (GET /api/v1/key). Kostet nichts.</summary>
public static class OpenRouterUsage
{
    public const string Host = "openrouter.ai";
    public const string KeyUrl = "https://openrouter.ai/api/v1/key";

    public static bool IsOpenRouter(string? baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var u) && u.Host.EndsWith(Host, StringComparison.OrdinalIgnoreCase);

    public static async Task<OpenRouterKeyUsage> GetAsync(HttpClient http, string apiKey, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, KeyUrl);
        Http.ApplyCommonHeaders(req, apiKey);
        var body = await Http.SendAsync(http, req, TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
        return Parse(body);
    }

    public static OpenRouterKeyUsage Parse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var d = doc.RootElement.TryGetProperty("data", out var data) ? data : doc.RootElement;
            double? Num(string name) => d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
            return new OpenRouterKeyUsage(Num("usage") ?? 0, Num("usage_daily"), Num("usage_weekly"), Num("usage_monthly"), Num("limit"), Num("limit_remaining"));
        }
        catch (JsonException ex)
        {
            throw new RemoteServiceException("invalid_response", ex);
        }
    }

    /// <summary>Verbrauch mehrerer Schlüssel zusammenzählen (Erkennung und Sprachmodell können verschiedene haben).</summary>
    public static OpenRouterKeyUsage Sum(IReadOnlyList<OpenRouterKeyUsage> list)
    {
        double? S(Func<OpenRouterKeyUsage, double?> f) => list.All(u => f(u) is null) ? null : list.Sum(u => f(u) ?? 0);
        return new OpenRouterKeyUsage(list.Sum(u => u.Total), S(u => u.Day), S(u => u.Week), S(u => u.Month),
            list.Count == 1 ? list[0].Limit : null, list.Count == 1 ? list[0].Remaining : null);
    }
}
