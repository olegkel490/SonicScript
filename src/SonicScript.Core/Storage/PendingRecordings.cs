using System.Text.Json;

namespace SonicScript.Core.Storage;

public sealed record PendingRecording(string AudioPath, DateTimeOffset CreatedAt, string ModeId, string Reason);

/// <summary>
/// Aufnahmen, deren Transkription fehlschlug, bleiben 24 h liegen und können erneut transkribiert werden.
/// </summary>
public sealed class PendingRecordings(string directory, TimeProvider? time = null)
{
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public PendingRecording Keep(string audioPath, string modeId, string reason)
    {
        Directory.CreateDirectory(directory);
        var now = _time.GetUtcNow();
        var target = Path.Combine(directory, $"rec-{now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.wav");
        File.Move(audioPath, target);
        var rec = new PendingRecording(target, now, modeId, reason);
        File.WriteAllText(target + ".json", JsonSerializer.Serialize(rec));
        return rec;
    }

    public IReadOnlyList<PendingRecording> List()
    {
        if (!Directory.Exists(directory)) return [];
        var result = new List<PendingRecording>();
        foreach (var meta in Directory.EnumerateFiles(directory, "*.wav.json"))
        {
            try
            {
                var rec = JsonSerializer.Deserialize<PendingRecording>(File.ReadAllText(meta));
                if (rec is not null && File.Exists(rec.AudioPath)) result.Add(rec);
            }
            catch (JsonException) { }
        }
        return result.OrderByDescending(r => r.CreatedAt).ToList();
    }

    public void Remove(PendingRecording rec)
    {
        TryDelete(rec.AudioPath);
        TryDelete(rec.AudioPath + ".json");
    }

    /// <summary>Älter als 24 h → löschen. Auch verwaiste Dateien ohne Metadaten.</summary>
    public int Cleanup()
    {
        if (!Directory.Exists(directory)) return 0;
        var cutoff = _time.GetUtcNow() - Retention;
        var n = 0;
        foreach (var f in Directory.EnumerateFiles(directory))
        {
            if (new DateTimeOffset(File.GetLastWriteTimeUtc(f), TimeSpan.Zero) < cutoff)
            {
                TryDelete(f);
                n++;
            }
        }
        return n;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
