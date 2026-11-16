using System.Text.RegularExpressions;
using SonicScript.Core.Settings;

namespace SonicScript.Core.Modes;

/// <summary>Ergebnis eines Sprachbefehls: anderer Modus und/oder als Notiz ablegen; <see cref="Rest"/> = eigentlicher Text.</summary>
public sealed record KeywordMatch(ModeDefinition Mode, bool IsNote, string Rest, string Keyword);

/// <summary>
/// Sprachbefehle am Anfang eines Diktats: "Prompt: Ich brauche …", "Englisch, wir sehen uns morgen",
/// "Notiz. Milch kaufen". Nach dem Stichwort muss ein Satzzeichen stehen (Whisper setzt es bei einer kurzen Pause) –
/// so löst "Englisch ist schwer" nichts aus.
/// </summary>
public static class ModeKeywords
{
    public static IEnumerable<string> Split(string? keywords) =>
        (keywords ?? "").Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public static IEnumerable<string> KeywordsOf(ModeDefinition m) =>
        Split(m.Keywords).DefaultIfEmpty(m.IsBuiltIn ? "" : m.Name.Trim()).Where(k => k.Length > 0);

    public static KeywordMatch? Detect(string text, AppSettings s)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var candidates = new List<(string Keyword, ModeDefinition? Mode, bool IsNote)>();
        foreach (var m in s.Modes)
            foreach (var k in KeywordsOf(m))
                candidates.Add((k, m, false));
        foreach (var k in Split(s.Notes.Keywords))
            candidates.Add((k, null, true));

        // Längste zuerst ("Auf Englisch" vor "Englisch").
        foreach (var (keyword, mode, isNote) in candidates.OrderByDescending(c => c.Keyword.Length))
        {
            var pattern = @"^\s*[„""'«]?" + Regex.Escape(keyword).Replace(@"\ ", @"\s+") + @"[""“'»]?\s*[:,.!;\-–—]+\s*(?<rest>.*)$";
            var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
            if (!m.Success) continue;
            var rest = UpperFirst(m.Groups["rest"].Value.Trim());
            var target = isNote ? s.FindMode(s.Notes.ModeId) ?? s.DefaultMode : mode!;
            return new KeywordMatch(target, isNote, rest, keyword);
        }
        return null;
    }

    private static string UpperFirst(string t) =>
        t.Length == 0 || !char.IsLower(t[0]) ? t : char.ToUpper(t[0]) + t[1..];
}
