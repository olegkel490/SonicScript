using System.Text.RegularExpressions;
using SonicScript.Core.Settings;

namespace SonicScript.Core.Text;

/// <summary>Feste Ersetzungen ("n acht n" → "n8n") und Schreibweisen aus dem Wörterbuch.</summary>
public sealed class DictionaryApplier
{
    private readonly List<(Regex Pattern, string To)> _rules = [];

    public DictionaryApplier(DictionarySettings dictionary)
    {
        foreach (var r in dictionary.Replacements)
        {
            if (string.IsNullOrWhiteSpace(r.From)) continue;
            _rules.Add((BuildPattern(r.From.Trim()), r.To ?? ""));
        }
        // Begriffe: falsche Groß-/Kleinschreibung korrigieren ("openrouter" → "OpenRouter").
        foreach (var term in dictionary.Terms)
        {
            if (string.IsNullOrWhiteSpace(term)) continue;
            _rules.Add((BuildPattern(term.Trim()), term.Trim()));
        }
    }

    private static Regex BuildPattern(string from)
    {
        var escaped = Regex.Escape(from).Replace(@"\ ", @"\s+");
        var start = char.IsLetterOrDigit(from[0]) ? @"(?<![\p{L}\p{N}])" : "";
        var end = char.IsLetterOrDigit(from[^1]) ? @"(?![\p{L}\p{N}])" : "";
        return new Regex(start + escaped + end, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    }

    public string Apply(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (var (pattern, to) in _rules)
            text = pattern.Replace(text, to.Replace("$", "$$"));
        return text;
    }
}
