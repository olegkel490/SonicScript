using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SonicScript.Core.Storage;

namespace SonicScript.Core.Notes;

public sealed record Note(int LineIndex, TimeOnly Time, string Text, bool Used);

/// <summary>
/// Blitznotizen als Markdown, eine Datei pro Tag: <c>{root}/2026/2026-11/2026-11-16.md</c>.
/// Zeile: <c>- [ ] 09:14 Text</c> (offen) bzw. <c>- [x] …</c> (verwendet). Lesbar in jedem Editor/Obsidian.
/// </summary>
public sealed partial class NoteStore(string rootFolder)
{
    private static readonly object Gate = new();

    [GeneratedRegex(@"^- \[(?<used>[ xX])\] (?<time>\d{2}:\d{2}) (?<text>.*)$")]
    private static partial Regex LinePattern();

    public string RootFolder { get; } = rootFolder;

    public string PathFor(DateOnly day) => Path.Combine(
        RootFolder,
        day.Year.ToString("0000", CultureInfo.InvariantCulture),
        day.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".md");

    public static string SingleLine(string text) =>
        Regex.Replace(text.Trim(), @"\s*\r?\n\s*", " ");

    public void Append(DateTime timestamp, string text)
    {
        var line = SingleLine(text);
        if (line.Length == 0) return;
        var day = DateOnly.FromDateTime(timestamp);
        var path = PathFor(day);
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var sb = new StringBuilder();
            if (!File.Exists(path))
                sb.Append("# ").Append(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("\n\n");
            else if (!EndsWithNewline(path))
                sb.Append('\n');
            sb.Append("- [ ] ").Append(timestamp.ToString("HH:mm", CultureInfo.InvariantCulture)).Append(' ').Append(line).Append('\n');
            File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }

    public IReadOnlyList<Note> Read(DateOnly day)
    {
        var path = PathFor(day);
        if (!File.Exists(path)) return [];
        string[] lines;
        lock (Gate) lines = File.ReadAllLines(path);
        var result = new List<Note>();
        for (var i = 0; i < lines.Length; i++)
        {
            var m = LinePattern().Match(lines[i]);
            if (!m.Success) continue;
            if (!TimeOnly.TryParseExact(m.Groups["time"].Value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) continue;
            result.Add(new Note(i, t, m.Groups["text"].Value, m.Groups["used"].Value != " "));
        }
        return result;
    }

    /// <summary>Markiert eine Notiz als verwendet. Prüft den Inhalt, falls die Datei extern geändert wurde.</summary>
    public bool MarkUsed(DateOnly day, Note note) => Rewrite(day, lines =>
    {
        if (note.LineIndex >= lines.Count) return false;
        var m = LinePattern().Match(lines[note.LineIndex]);
        if (!m.Success || m.Groups["text"].Value != note.Text) return false;
        lines[note.LineIndex] = "- [x] " + lines[note.LineIndex][6..];
        return true;
    });

    public int DeleteUsed(DateOnly day)
    {
        var removed = 0;
        Rewrite(day, lines =>
        {
            removed = lines.RemoveAll(l => LinePattern().Match(l) is { Success: true } m && m.Groups["used"].Value != " ");
            return removed > 0;
        });
        DeleteIfEmpty(day);
        return removed;
    }

    public int DeleteAll(DateOnly day)
    {
        var removed = 0;
        Rewrite(day, lines =>
        {
            removed = lines.RemoveAll(l => LinePattern().IsMatch(l));
            return removed > 0;
        });
        DeleteIfEmpty(day);
        return removed;
    }

    /// <summary>Tage mit Notizdatei in einem Monat (für die Monatsnavigation).</summary>
    public IReadOnlyList<DateOnly> DaysWithNotes(int year, int month)
    {
        var dir = Path.GetDirectoryName(PathFor(new DateOnly(year, month, 1)))!;
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.md")
            .Select(f => DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(f), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateOnly?)null)
            .Where(d => d is not null)
            .Select(d => d!.Value)
            .OrderBy(d => d)
            .ToList();
    }

    private bool Rewrite(DateOnly day, Func<List<string>, bool> change)
    {
        var path = PathFor(day);
        lock (Gate)
        {
            if (!File.Exists(path)) return false;
            var lines = File.ReadAllLines(path).ToList();
            if (!change(lines)) return false;
            AtomicFile.WriteAllText(path, string.Join('\n', lines) + "\n");
            return true;
        }
    }

    private void DeleteIfEmpty(DateOnly day)
    {
        var path = PathFor(day);
        lock (Gate)
        {
            if (!File.Exists(path)) return;
            var meaningful = File.ReadAllLines(path).Any(l => l.Trim().Length > 0 && !l.StartsWith("# ", StringComparison.Ordinal));
            if (!meaningful) File.Delete(path);
        }
    }

    private static bool EndsWithNewline(string path)
    {
        using var fs = File.OpenRead(path);
        if (fs.Length == 0) return true;
        fs.Seek(-1, SeekOrigin.End);
        return fs.ReadByte() == '\n';
    }
}
