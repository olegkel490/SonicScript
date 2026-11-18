using System.Text.Json;
using System.Text.RegularExpressions;

namespace SonicScript.Core.Stats;

public sealed class DayStats
{
    public int Dictations { get; set; }
    public int Words { get; set; }
    public double AudioSeconds { get; set; }
    public Dictionary<string, int> Modes { get; set; } = [];
}

public sealed record StatsSummary(int Dictations, int Words, TimeSpan Spoken, TimeSpan Saved, IReadOnlyList<(string ModeId, int Count)> TopModes);

/// <summary>
/// Nutzungsstatistik pro Tag (nur Zahlen, kein Text): Diktate, Wörter, Sprechzeit, Modi.
/// Gesparte Zeit = Tippzeit bei <see cref="TypingWordsPerMinute"/> minus Sprechzeit.
/// </summary>
public sealed partial class UsageStats(string path)
{
    public const double TypingWordsPerMinute = 40;
    private readonly object _gate = new();
    private Dictionary<string, DayStats>? _days;

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordRegex();

    public static int CountWords(string text) => WordRegex().Count(text ?? "");

    public void Add(DateOnly day, string modeId, string text, TimeSpan audio)
    {
        lock (_gate)
        {
            var days = Load();
            var key = day.ToString("yyyy-MM-dd");
            if (!days.TryGetValue(key, out var d)) days[key] = d = new DayStats();
            d.Dictations++;
            d.Words += CountWords(text);
            d.AudioSeconds += Math.Max(0, audio.TotalSeconds);
            d.Modes[modeId] = d.Modes.GetValueOrDefault(modeId) + 1;
            Save(days);
        }
    }

    /// <summary>Zusammenfassung für [from, to] (beide inklusive); from = null → seit Beginn.</summary>
    public StatsSummary Summary(DateOnly? from, DateOnly to)
    {
        lock (_gate)
        {
            var sel = Load().Where(kv => DateOnly.TryParse(kv.Key, out var d) && (from is null || d >= from) && d <= to).Select(kv => kv.Value).ToList();
            var words = sel.Sum(d => d.Words);
            var spoken = TimeSpan.FromSeconds(sel.Sum(d => d.AudioSeconds));
            var typing = TimeSpan.FromMinutes(words / TypingWordsPerMinute);
            var saved = typing > spoken ? typing - spoken : TimeSpan.Zero;
            var modes = sel.SelectMany(d => d.Modes).GroupBy(kv => kv.Key)
                .Select(g => (g.Key, g.Sum(kv => kv.Value))).OrderByDescending(t => t.Item2).Take(5).ToList();
            return new StatsSummary(sel.Sum(d => d.Dictations), words, spoken, saved, modes);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _days = [];
            Save(_days);
        }
    }

    private Dictionary<string, DayStats> Load()
    {
        if (_days is not null) return _days;
        try
        {
            _days = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, DayStats>>(File.ReadAllText(path)) ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _days = [];
        }
        return _days;
    }

    private void Save(Dictionary<string, DayStats> days)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Storage.AtomicFile.WriteAllText(path, JsonSerializer.Serialize(days));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Statistik ist nicht wichtig genug für eine Fehlermeldung.
        }
    }
}
