using System.Net;
using System.Net.Sockets;

namespace SonicScript.Core.Net;

/// <summary>
/// Schützt Geheimnisse: Ein API-Schlüssel darf nur über https oder an lokale/private Adressen
/// (localhost, LAN, Tailscale 100.64.0.0/10) gehen.
/// </summary>
public static class EndpointPolicy
{
    public static bool TryValidate(string? baseUrl, bool hasSecret, out string? error)
    {
        error = null;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            error = "invalid_url";
            return false;
        }
        if (uri.Scheme == Uri.UriSchemeHttp && hasSecret && !IsLocalOrPrivate(uri))
        {
            error = "insecure_http";
            return false;
        }
        return true;
    }

    public static bool IsLocalOrPrivate(string? baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && IsLocalOrPrivate(uri);

    public static bool IsLocalOrPrivate(Uri uri)
    {
        var host = uri.IdnHost;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase)) return true; // Tailscale MagicDNS
        if (!IPAddress.TryParse(host.Trim('[', ']'), out var ip)) return false;
        if (IPAddress.IsLoopback(ip)) return true;

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            else return ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal;
        }

        var b = ip.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127); // CGNAT / Tailscale
    }
}
