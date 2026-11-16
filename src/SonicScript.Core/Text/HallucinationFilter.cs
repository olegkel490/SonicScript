using System.Text.RegularExpressions;

namespace SonicScript.Core.Text;

/// <summary>
/// Whisper erfindet bei Stille oder Rauschen gern Abspann-Floskeln ("Untertitel im Auftrag des ZDF",
/// "Thanks for watching!"). Diese werden entfernt, ebenso Marker wie [BLANK_AUDIO] oder (Musik).
/// </summary>
public static partial class HallucinationFilter
{
    private static readonly string[] KnownPhrases =
    [
        "untertitel im auftrag des zdf",
        "untertitelung im auftrag des zdf",
        "untertitelung des zdf",
        "untertitel der amara.org-community",
        "untertitel von stephanie geiges",
        "vielen dank fürs zuschauen",
        "vielen dank für's zuschauen",
        "danke fürs zuschauen",
        "bis zum nächsten mal",
        "tschüss",
        "copyright wdr",
        "swr",
        "thank you for watching",
        "thanks for watching",
        "thank you",
        "please subscribe",
        "subtitles by the amara.org community",
        "sous-titres réalisés para la communauté d'amara.org",
        "sous-titrage st' 501",
    ];

    // Floskeln, die auch am Ende eines echten Diktats angehängt werden (nicht "danke" – das kann echt sein).
    private static readonly string[] TrailingPhrases =
    [
        "untertitel im auftrag des zdf",
        "untertitelung im auftrag des zdf",
        "untertitelung des zdf",
        "untertitel der amara.org-community",
        "subtitles by the amara.org community",
        "copyright wdr",
    ];

    [GeneratedRegex(@"\[(?:BLANK_AUDIO|MUSIC|Musik|Music|Applaus|Applause|Lachen|Laughter|Stille|Silence|NO_SPEECH|inaudible|unverständlich)[^\]]*\]|\((?:Musik|Music|Applaus|Applause|Lachen|Laughter|Stille|Silence)[^)]*\)|\*(?:Musik|Music)\*|[♪♫]+", RegexOptions.IgnoreCase)]
    private static partial Regex NonSpeechMarkers();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpace();

    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var t = NonSpeechMarkers().Replace(text, " ");
        t = MultiSpace().Replace(t, " ").Trim();
        if (t.Length == 0) return "";

        if (IsKnownPhrase(t)) return "";

        // Angehängte Abspann-Floskel am Ende entfernen.
        foreach (var phrase in TrailingPhrases)
        {
            var idx = t.LastIndexOf(phrase, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                var tail = t[(idx + phrase.Length)..];
                if (tail.All(c => char.IsDigit(c) || char.IsWhiteSpace(c) || char.IsPunctuation(c)))
                {
                    t = t[..idx].TrimEnd(' ', ',', '-', '–');
                    break;
                }
            }
        }
        return t.Trim();
    }

    private static bool IsKnownPhrase(string t)
    {
        var norm = Normalize(t);
        foreach (var p in KnownPhrases)
        {
            if (norm == p) return true;
            // "Copyright WDR 2021", "Untertitelung des ZDF, 2020"
            if (norm.StartsWith(p, StringComparison.Ordinal) &&
                norm[p.Length..].All(c => char.IsDigit(c) || c == ' ' || c == ','))
                return true;
        }
        return false;
    }

    private static string Normalize(string t) =>
        MultiSpace().Replace(t.ToLowerInvariant().Trim().TrimEnd('.', '!', '?', '…').Trim(), " ");
}
