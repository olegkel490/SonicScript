using System.Text.RegularExpressions;

namespace SonicScript.Core.Text;

/// <summary>Vom Nutzer korrigiertes Wort: so wurde es erkannt (<see cref="From"/>) und so geschrieben (<see cref="To"/>).</summary>
public sealed record CorrectionSuggestion(string From, string To);

/// <summary>
/// Vergleicht den eingefügten Text mit dem, was später im Feld steht, und findet einzelne korrigierte Wörter
/// ("Opnrauter" → "OpenRouter"). Größere Umformulierungen werden ignoriert – es geht nur um Erkennungsfehler.
/// </summary>
public static partial class CorrectionDetector
{
    [GeneratedRegex(@"[\p{L}\p{N}](?:[\p{L}\p{N}\-_'’.]*[\p{L}\p{N}])?")]
    private static partial Regex Word();

    private const int MaxInserted = 400;
    private const int MaxCurrent = 8000;

    public static IReadOnlyList<CorrectionSuggestion> Detect(string inserted, string current)
    {
        var a = Tokens(inserted);
        if (a.Count < 2 || string.IsNullOrWhiteSpace(current)) return [];
        if (a.Count > MaxInserted) a = a.GetRange(0, MaxInserted);
        var b = Tokens(current);
        if (b.Count > MaxCurrent) b = b.GetRange(b.Count - MaxCurrent, MaxCurrent);

        var ops = Align(a, b);
        if (ops is null) return [];

        // Zu viel verändert → der Nutzer hat umformuliert, nicht korrigiert.
        var changed = ops.Count(o => o.Kind == Op.Del);
        var matched = ops.Count(o => o.Kind == Op.Match);
        if (matched < 2 || changed > Math.Max(3, a.Count * 0.35)) return [];

        var result = new List<CorrectionSuggestion>();
        for (var i = 0; i < ops.Count; i++)
        {
            if (ops[i].Kind == Op.Match) continue;
            var j = i;
            while (j < ops.Count && ops[j].Kind != Op.Match) j++;
            // Lauf [i, j) aus Nicht-Treffern, eingerahmt von Treffern (oder Rand).
            var from = ops.Skip(i).Take(j - i).Where(o => o.A >= 0).Select(o => a[o.A]).ToList();
            var to = ops.Skip(i).Take(j - i).Where(o => o.B >= 0).Select(o => b[o.B]).ToList();
            var leftOk = i > 0 && ops[i - 1].Kind == Op.Match;
            var rightOk = j < ops.Count && ops[j].Kind == Op.Match;
            if (leftOk && rightOk && to.Count == 1 && from.Count is >= 1 and <= 3 && IsCorrection(from, to[0]))
                result.Add(new CorrectionSuggestion(string.Join(" ", from), to[0]));
            i = j - 1;
        }
        return result.DistinctBy(r => (r.From.ToLowerInvariant(), r.To)).ToList();
    }

    private static bool IsCorrection(List<string> from, string to)
    {
        if (to.Length < 2) return false;
        var joined = string.Concat(from);
        if (from.Count > 1)
            return Squash(joined) == Squash(to); // "Open Router" → "OpenRouter"
        var f = from[0];
        if (f == to) return false;
        // Nur Großschreibung am Wortanfang geändert = Satzanfang, keine Schreibweise.
        if (f.Length > 1 && f[1..] == to[1..] && char.ToLowerInvariant(f[0]) == char.ToLowerInvariant(to[0])) return false;
        if (string.Equals(f, to, StringComparison.OrdinalIgnoreCase)) return true; // "openrouter" → "OpenRouter"
        if (f.Length < 3) return false;
        return Similarity(f.ToLowerInvariant(), to.ToLowerInvariant()) >= 0.6;
    }

    private static string Squash(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    internal static double Similarity(string x, string y)
    {
        var d = new int[x.Length + 1, y.Length + 1];
        for (var i = 0; i <= x.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= y.Length; j++) d[0, j] = j;
        for (var i = 1; i <= x.Length; i++)
            for (var j = 1; j <= y.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (x[i - 1] == y[j - 1] ? 0 : 1));
        return 1.0 - (double)d[x.Length, y.Length] / Math.Max(x.Length, y.Length);
    }

    private static List<string> Tokens(string text) => Word().Matches(text).Select(m => m.Value).ToList();

    private enum Op { Match, Sub, Del, Ins }
    private readonly record struct Step(Op Kind, int A, int B);

    /// <summary>Wort-Alignment: <paramref name="a"/> komplett gegen einen beliebigen Ausschnitt von <paramref name="b"/>.</summary>
    private static List<Step>? Align(List<string> a, List<string> b)
    {
        int n = a.Count, m = b.Count;
        if ((long)n * m > 4_000_000) return null;
        var d = new int[n + 1, m + 1];
        for (var i = 1; i <= n; i++) d[i, 0] = i;
        // d[0, j] = 0: Anfang in b frei
        for (var i = 1; i <= n; i++)
            for (var j = 1; j <= m; j++)
            {
                var eq = string.Equals(a[i - 1], b[j - 1], StringComparison.Ordinal);
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (eq ? 0 : 1));
            }
        // Ende in b frei: bestes j in der letzten Zeile
        var bj = 0;
        for (var j = 1; j <= m; j++) if (d[n, j] < d[n, bj]) bj = j;

        var steps = new List<Step>();
        int x = n, y = bj;
        while (x > 0)
        {
            if (y > 0 && d[x, y] == d[x - 1, y - 1] + (a[x - 1] == b[y - 1] ? 0 : 1))
            {
                steps.Add(new Step(a[x - 1] == b[y - 1] ? Op.Match : Op.Sub, x - 1, y - 1));
                x--; y--;
            }
            else if (d[x, y] == d[x - 1, y] + 1)
            {
                steps.Add(new Step(Op.Del, x - 1, -1));
                x--;
            }
            else if (y > 0)
            {
                steps.Add(new Step(Op.Ins, -1, y - 1));
                y--;
            }
            else break;
        }
        steps.Reverse();
        // Eine Ersetzung als Löschen + Einfügen darstellen, damit Läufe einheitlich sind.
        return steps.SelectMany(s => s.Kind == Op.Sub ? [new Step(Op.Del, s.A, -1), new Step(Op.Ins, -1, s.B)] : new[] { s }).ToList();
    }
}
