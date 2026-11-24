using System.Diagnostics;
using System.Security.Cryptography;
using SonicScript.Core.Updates;

namespace SonicScript.App.Services;

public enum UpdateResult
{
    Ready,
    NoDownload,
    NoWriteAccess,
    ChecksumMismatch,
}

/// <summary>
/// Update in der App: neue .exe neben die laufende laden, SHA-256 prüfen, laufende .exe in *.old umbenennen
/// (Windows erlaubt das bei laufenden Programmen), neue an ihre Stelle setzen und mit --after-update starten.
/// Die neue Instanz wartet, bis die alte beendet ist, und löscht dann *.old.
/// </summary>
public sealed class Updater(HttpClient http)
{
    public const string AfterUpdateArg = "--after-update";
    public const string RolledBackArg = "--rolled-back";
    /// <summary>Die neue Version setzt dieses Ereignis, sobald sie stabil läuft.</summary>
    public const string HealthyEventName = @"Local\SonicScript.UpdateHealthy";

    /// <summary>Nach <see cref="UpdateResult.Ready"/>: die gestartete neue Version und ihr Gesundheits-Signal.</summary>
    public Process? Started { get; private set; }
    public EventWaitHandle? Healthy { get; private set; }

    public async Task<UpdateResult> InstallAsync(ReleaseInfo release, CancellationToken ct)
    {
        if (release.DownloadUrl is null) return UpdateResult.NoDownload;
        var exe = Environment.ProcessPath;
        if (exe is null || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return UpdateResult.NoDownload;

        var dir = Path.GetDirectoryName(exe)!;
        var newPath = exe + ".new";
        var oldPath = exe + ".old";

        try
        {
            await using (var src = await http.GetStreamAsync(release.DownloadUrl, ct))
            await using (var dst = File.Create(newPath))
                await src.CopyToAsync(dst, ct);
        }
        catch (UnauthorizedAccessException)
        {
            return UpdateResult.NoWriteAccess;
        }

        if (release.ChecksumUrl is not null)
        {
            var expected = UpdateChecker.ParseSha256(await http.GetStringAsync(release.ChecksumUrl, ct));
            string actual;
            await using (var fs = File.OpenRead(newPath))
                actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(fs, ct));
            if (expected is null || expected != actual)
            {
                File.Delete(newPath);
                return UpdateResult.ChecksumMismatch;
            }
        }

        if (File.Exists(oldPath)) File.Delete(oldPath);
        File.Move(exe, oldPath);
        try
        {
            File.Move(newPath, exe);
        }
        catch
        {
            File.Move(oldPath, exe); // zurückrollen
            throw;
        }

        Healthy = new EventWaitHandle(false, EventResetMode.ManualReset, HealthyEventName);
        Started = Process.Start(new ProcessStartInfo(exe, AfterUpdateArg) { UseShellExecute = false, WorkingDirectory = dir });
        Log.Info($"Update auf {release.Tag} installiert, Neustart");
        return UpdateResult.Ready;
    }

    /// <summary>Neue Version läuft stabil: der wartenden alten Instanz Bescheid geben.</summary>
    public static void SignalHealthy()
    {
        if (EventWaitHandle.TryOpenExisting(HealthyEventName, out var ev))
        {
            using (ev) ev.Set();
        }
    }

    /// <summary>
    /// Neue Version ist abgestürzt oder hängt: sie beenden, alte Version zurück an ihren Platz und starten.
    /// Läuft in der alten Instanz (deren Datei heißt gerade *.old).
    /// </summary>
    public static bool RollBack(Process? started)
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return false;
        var oldPath = exe + ".old";
        try
        {
            if (started is { HasExited: false })
            {
                started.Kill();
                started.WaitForExit(5000);
            }
        }
        catch (Exception) { }
        for (var i = 0; i < 20; i++)
        {
            try
            {
                if (!File.Exists(oldPath)) return false;
                if (File.Exists(exe)) File.Move(exe, exe + ".bad", overwrite: true);
                File.Move(oldPath, exe);
                Process.Start(new ProcessStartInfo(exe, $"{AfterUpdateArg} {RolledBackArg}") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
                return true;
            }
            catch (IOException) { Thread.Sleep(250); }
            catch (UnauthorizedAccessException) { Thread.Sleep(250); }
        }
        return false;
    }

    /// <summary>Beim Start nach einem Update: Reste der alten Version entfernen.</summary>
    public static void CleanupOldVersion()
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return;
        foreach (var leftover in new[] { exe + ".old", exe + ".new", exe + ".bad" })
        {
            for (var i = 0; i < 20 && File.Exists(leftover); i++)
            {
                try { File.Delete(leftover); }
                catch (IOException) { Thread.Sleep(250); }
                catch (UnauthorizedAccessException) { Thread.Sleep(250); }
            }
        }
    }
}
