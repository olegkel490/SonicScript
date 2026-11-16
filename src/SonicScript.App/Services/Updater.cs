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

        Process.Start(new ProcessStartInfo(exe, AfterUpdateArg) { UseShellExecute = false, WorkingDirectory = dir });
        Log.Info($"Update auf {release.Tag} installiert, Neustart");
        return UpdateResult.Ready;
    }

    /// <summary>Beim Start nach einem Update: Reste der alten Version entfernen.</summary>
    public static void CleanupOldVersion()
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return;
        foreach (var leftover in new[] { exe + ".old", exe + ".new" })
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
