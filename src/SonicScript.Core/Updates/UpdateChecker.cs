using System.Net.Http.Headers;
using System.Text.Json;

namespace SonicScript.Core.Updates;

public sealed record ReleaseInfo(Version Version, string Tag, string HtmlUrl, string? DownloadUrl, string? ChecksumUrl = null);

/// <summary>Prüft die GitHub-Releases auf eine neuere Version. Lädt nichts herunter.</summary>
public sealed class UpdateChecker(HttpClient http, string repository = "olegkel490/SonicScript")
{
    public async Task<ReleaseInfo?> GetNewerReleaseAsync(Version current, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("SonicScript", current.ToString(3)));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        var release = Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return release is not null && release.Version > current ? release : null;
    }

    public static ReleaseInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var p) && p.GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!TryParseVersion(tag, out var v)) return null;
        string? exe = null, sha = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                var url = a.GetProperty("browser_download_url").GetString();
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe ??= url;
                else if (name.EndsWith(".exe.sha256", StringComparison.OrdinalIgnoreCase)) sha ??= url;
            }
        }
        return new ReleaseInfo(v, tag, root.GetProperty("html_url").GetString() ?? "", exe, sha);
    }

    /// <summary>Liest den Hash aus einer sha256sum-Zeile ("&lt;hex&gt;  SonicScript.exe").</summary>
    public static string? ParseSha256(string content)
    {
        var first = content.Trim().Split((char[])[' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is { Length: 64 } && first.All(Uri.IsHexDigit) ? first.ToLowerInvariant() : null;
    }

    public static bool TryParseVersion(string tag, out Version version)
    {
        var t = tag.Trim().TrimStart('v', 'V');
        var dash = t.IndexOf('-');
        if (dash >= 0) t = t[..dash];
        if (Version.TryParse(t, out var v))
        {
            version = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            return true;
        }
        version = new Version(0, 0, 0);
        return false;
    }
}
