namespace SonicScript.App;

/// <summary>Kleines Datei-Log ohne Inhalte von Diktaten (nur Ereignisse und Fehler).</summary>
public static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 1024 * 1024;

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? ex) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                var path = AppPaths.LogFile;
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                    File.Move(path, path + ".1", overwrite: true);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{(ex is null ? "" : " | " + ex)}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
