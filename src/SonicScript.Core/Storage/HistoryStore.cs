using System.Text.Json;

namespace SonicScript.Core.Storage;

public sealed record HistoryEntry(DateTimeOffset Timestamp, string ModeId, string Verbatim, string Final, string? App);

/// <summary>Verlauf der letzten Ergebnisse, verschlüsselt auf der Platte, begrenzt nach Anzahl und Alter.</summary>
public sealed class HistoryStore(string path, ISecretProtector protector, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly object _gate = new();
    private List<HistoryEntry>? _cache;

    public IReadOnlyList<HistoryEntry> GetAll()
    {
        lock (_gate) return Load().ToList();
    }

    public void Add(HistoryEntry entry, int maxEntries, int maxAgeDays)
    {
        lock (_gate)
        {
            var list = Load();
            list.Insert(0, entry);
            Prune(list, maxEntries, maxAgeDays);
            Save(list);
        }
    }

    public void Prune(int maxEntries, int maxAgeDays)
    {
        lock (_gate)
        {
            var list = Load();
            var before = list.Count;
            Prune(list, maxEntries, maxAgeDays);
            if (list.Count != before) Save(list);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _cache = [];
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private void Prune(List<HistoryEntry> list, int maxEntries, int maxAgeDays)
    {
        var cutoff = _time.GetUtcNow() - TimeSpan.FromDays(maxAgeDays);
        list.RemoveAll(e => e.Timestamp < cutoff);
        if (list.Count > maxEntries) list.RemoveRange(maxEntries, list.Count - maxEntries);
    }

    private List<HistoryEntry> Load()
    {
        if (_cache is not null) return _cache;
        _cache = [];
        if (!File.Exists(path)) return _cache;
        try
        {
            var plain = protector.Unprotect(File.ReadAllBytes(path));
            _cache = JsonSerializer.Deserialize<List<HistoryEntry>>(plain) ?? [];
        }
        catch (Exception)
        {
            // Nicht lesbar (anderer Benutzer, beschädigt) → neu beginnen.
            _cache = [];
        }
        return _cache;
    }

    private void Save(List<HistoryEntry> list)
    {
        _cache = list;
        AtomicFile.WriteAllBytes(path, protector.Protect(JsonSerializer.SerializeToUtf8Bytes(list)));
    }
}
