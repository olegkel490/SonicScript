namespace SonicScript.Core.Storage;

public static class AtomicFile
{
    /// <summary>Schreibt erst in eine Temp-Datei und ersetzt dann – kein halb geschriebenes File nach Absturz.</summary>
    public static void WriteAllBytes(string path, byte[] data)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(data);
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    public static void WriteAllText(string path, string text) =>
        WriteAllBytes(path, new System.Text.UTF8Encoding(false).GetBytes(text));
}
