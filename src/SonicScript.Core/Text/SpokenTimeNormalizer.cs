using System.Text.RegularExpressions;

namespace SonicScript.Core.Text;

/// <summary>
/// Gesprochene Uhrzeiten als Uhrzeiten schreiben (Deutsch):
/// "sechzehn Uhr fünfundvierzig bis siebzehn Uhr fünf" → "16:45 bis 17:05 Uhr",
/// "um acht Uhr" → "um 8 Uhr", "10 Uhr 30" → "10:30 Uhr".
/// </summary>
public static class SpokenTimeNormalizer
{
    private static readonly string Num = $@"(?:\d{{1,2}}|{GermanNumbers.Pattern})";

    // Stunde "Uhr" [Minute]; Minute darf kein Teil eines Folgeworts sein.
    // "ein"/"eine" als Minute ausschließen ("zehn Uhr eine Besprechung"), ebenso "… Minuten".
    private static readonly string TimeCore =
        $@"(?<h>{Num})\s+Uhr(?:\s+(?!eine?(?!\p{{L}}))(?<m>{Num})(?![\p{{L}}\d:])(?!\s+Minute))?(?![\p{{L}}\d:])";

    private static readonly Regex Range = new(
        $@"(?<![\p{{L}}\d:]){Rename(TimeCore, "1")}\s+bis\s+{Rename(TimeCore, "2")}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Single = new(
        $@"(?<![\p{{L}}\d:]){TimeCore}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string Rename(string pattern, string suffix) =>
        pattern.Replace("?<h>", $"?<h{suffix}>").Replace("?<m>", $"?<m{suffix}>");

    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf("uhr", StringComparison.OrdinalIgnoreCase) < 0) return text;

        text = Range.Replace(text, m =>
        {
            var a = Format(m.Groups["h1"].Value, m.Groups["m1"]);
            var b = Format(m.Groups["h2"].Value, m.Groups["m2"]);
            return a is null || b is null ? m.Value : $"{a} bis {b} Uhr";
        });

        return Single.Replace(text, m =>
        {
            var t = Format(m.Groups["h"].Value, m.Groups["m"]);
            return t is null ? m.Value : t + " Uhr";
        });
    }

    private static string? Format(string hourText, Group minuteGroup)
    {
        var h = GermanNumbers.Parse(hourText);
        if (h is null or < 0 or > 24) return null;
        if (!minuteGroup.Success) return h.Value.ToString();
        var min = GermanNumbers.Parse(minuteGroup.Value);
        if (min is null or < 0 or > 59) return null;
        return $"{h.Value}:{min.Value:00}";
    }
}
