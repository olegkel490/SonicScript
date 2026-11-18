using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;

namespace SonicScript.Platform.Windows;

/// <summary>Absturzinfos aus der Windows-Ereignisanzeige und automatischer Neustart nach Absturz.</summary>
public static class CrashReports
{
    /// <summary>
    /// Einträge "Application Error" (1000) und ".NET Runtime" (1025/1026) zu <paramref name="exeName"/> seit
    /// <paramref name="since"/> – enthalten Fehlermodul, Ausnahmecode bzw. Meldung und Stack der .NET-Laufzeit.
    /// </summary>
    public static IReadOnlyList<string> Read(string exeName, DateTime since, int max = 5)
    {
        var result = new List<string>();
        try
        {
            var ms = (long)Math.Max(1000, (DateTime.Now - since).TotalMilliseconds);
            var query = new EventLogQuery("Application", PathType.LogName,
                $"*[System[(Provider[@Name='Application Error'] or Provider[@Name='.NET Runtime']) and TimeCreated[timediff(@SystemTime) <= {ms}]]]")
            { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            for (var ev = reader.ReadEvent(); ev is not null && result.Count < max; ev = reader.ReadEvent())
            {
                using (ev)
                {
                    string text;
                    try { text = ev.FormatDescription() ?? string.Join(" | ", ev.Properties.Select(p => p.Value)); }
                    catch (EventLogException) { text = string.Join(" | ", ev.Properties.Select(p => p.Value)); }
                    if (!text.Contains(exeName, StringComparison.OrdinalIgnoreCase)) continue;
                    result.Add($"{ev.TimeCreated:yyyy-MM-dd HH:mm:ss} {ev.ProviderName} {ev.Id}: {text}");
                }
            }
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or InvalidOperationException)
        {
            result.Add("Ereignisanzeige nicht lesbar: " + ex.Message);
        }
        return result;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterApplicationRestart(string? commandLine, int flags);

    private const int RESTART_NO_PATCH = 4;
    private const int RESTART_NO_REBOOT = 8;

    /// <summary>
    /// Windows startet die App nach einem Absturz oder Hänger neu (nur wenn sie mindestens 60 s lief,
    /// nicht nach Beenden durch Virenscanner/Task-Manager).
    /// </summary>
    public static bool EnableAutoRestart(string arguments) =>
        RegisterApplicationRestart(arguments, RESTART_NO_PATCH | RESTART_NO_REBOOT) == 0;
}

/// <summary>Fehler aus Windows-Callbacks melden (dürfen dort nie hochgeworfen werden).</summary>
public static class PlatformErrors
{
    public static Action<string, Exception>? Report { get; set; }
}
