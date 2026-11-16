using System.Net.Http.Headers;
using System.Text.Json;

namespace SonicScript.Core.Net;

/// <summary>Fehler eines entfernten/lokalen Dienstes. <see cref="Exception.Message"/> beginnt mit einem Code (z. B. "timeout", "http_401").</summary>
public sealed class RemoteServiceException(string message, Exception? inner = null) : Exception(message, inner);

internal static class Http
{
    public static void ApplyCommonHeaders(HttpRequestMessage req, string? apiKey)
    {
        if (apiKey is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Headers.TryAddWithoutValidation("X-Title", "SonicScript");
        req.Headers.ExpectContinue = false;
    }

    public static async Task<string> SendAsync(HttpClient http, HttpRequestMessage req, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new RemoteServiceException($"http_{(int)resp.StatusCode}: {ExtractError(body)}");
            return body;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new RemoteServiceException("timeout");
        }
        catch (HttpRequestException ex)
        {
            throw new RemoteServiceException("unreachable: " + ex.Message, ex);
        }
    }

    public static string ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e))
            {
                if (e.ValueKind == JsonValueKind.String) return e.GetString() ?? "";
                if (e.TryGetProperty("message", out var m)) return m.GetString() ?? "";
            }
        }
        catch (JsonException) { }
        return body.Length > 200 ? body[..200] : body;
    }
}
