using Avalonia.Threading;
using SonicScript.Platform.Windows;

namespace SonicScript.App;

/// <summary>
/// Merkt sich, dass SonicScript läuft (Datei + Lebenszeichen jede Minute). Fehlt beim nächsten Start das saubere Ende,
/// werden die Windows-Absturzinfos ins Log geschrieben.
/// </summary>
public static class CrashGuard
{
    private static string Marker => Path.Combine(AppPaths.Local, "running.marker");
    private static DispatcherTimer? _heartbeat;

    /// <summary>Beim Start aufrufen. Liefert die gefundenen Absturzeinträge, falls der letzte Lauf abgestürzt ist.</summary>
    public static IReadOnlyList<string> Start(string version)
    {
        IReadOnlyList<string> crashes = [];
        try
        {
            if (File.Exists(Marker) && !PreviousStillRunning(File.ReadAllText(Marker)))
            {
                var lastAlive = File.GetLastWriteTime(Marker);
                var info = File.ReadAllText(Marker).Trim();
                Log.Info($"Previous run ({info}) ended without clean exit; last sign of life {lastAlive:yyyy-MM-dd HH:mm:ss}");
                if (OperatingSystem.IsWindows())
                {
                    crashes = CrashReports.Read("SonicScript.exe", lastAlive.AddMinutes(-2));
                    foreach (var c in crashes) Log.Info("Windows crash report: " + c);
                    if (crashes.Count == 0) Log.Info("No Windows crash report (shutdown, sleep or ended by another program?)");
                }
            }
            File.WriteAllText(Marker, $"v{version}, pid {Environment.ProcessId}, started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Crash guard", ex);
        }

        _heartbeat = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _heartbeat.Tick += (_, _) =>
        {
            try { File.SetLastWriteTime(Marker, DateTime.Now); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        };
        _heartbeat.Start();

        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (!CrashReports.EnableAutoRestart("--background")) Log.Info("Auto restart not registered");
            }
            catch (Exception ex) { Log.Error("Auto restart", ex); }
        }
        return crashes.Where(c => !c.StartsWith("Ereignisanzeige", StringComparison.Ordinal)).ToList();
    }

    /// <summary>Bei Update/Umzug läuft die alte Instanz noch kurz – das ist kein Absturz.</summary>
    private static bool PreviousStillRunning(string marker)
    {
        var m = System.Text.RegularExpressions.Regex.Match(marker, @"pid (\d+),");
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out var pid) || pid == Environment.ProcessId) return false;
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            return !p.HasExited && p.ProcessName.Contains("SonicScript", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    /// <summary>Sauberes Ende (Beenden, Update, Abmelden).</summary>
    public static void Stop(string reason)
    {
        _heartbeat?.Stop();
        Log.Info("Exit: " + reason);
        try
        {
            // Nach einem Update/Umzug schreibt die neue Instanz schon ihren eigenen Marker – den nicht löschen.
            if (File.Exists(Marker) && File.ReadAllText(Marker).Contains($"pid {Environment.ProcessId},", StringComparison.Ordinal))
                File.Delete(Marker);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
