namespace SonicScript.App;

/// <summary>
/// Wo die .exe liegt. In OneDrive/Dropbox-Ordnern sperrt die Synchronisation die Datei gelegentlich (Updates scheitern)
/// und Virenscanner prüfen dort strenger – empfohlen ist %LOCALAPPDATA%\Programs\SonicScript.
/// </summary>
public sealed record AppLocation(string ExePath, bool IsSynced, bool CanMove)
{
    public const string MovedArg = "--moved-from";

    public static string RecommendedPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SonicScript", "SonicScript.exe");

    public static AppLocation Current
    {
        get
        {
            var exe = Environment.ProcessPath ?? "";
            var synced = IsSyncedFolder(exe);
            var isExe = exe.EndsWith("SonicScript.exe", StringComparison.OrdinalIgnoreCase);
            var atTarget = string.Equals(Path.GetFullPath(exe), Path.GetFullPath(RecommendedPath), StringComparison.OrdinalIgnoreCase);
            return new AppLocation(exe, synced, synced && isExe && !atTarget);
        }
    }

    public static bool IsSyncedFolder(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        foreach (var v in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
        {
            var root = Environment.GetEnvironmentVariable(v);
            if (!string.IsNullOrWhiteSpace(root) && path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return path.Contains(@"\OneDrive", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\Dropbox\", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\Google Drive\", StringComparison.OrdinalIgnoreCase);
    }
}
