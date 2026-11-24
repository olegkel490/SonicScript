using System.Globalization;
using System.Text.RegularExpressions;

namespace SonicScript.Core.Reminders;

public sealed record ReminderMatch(DateTime Due, string Expression);

/// <summary>
/// Findet Zeitangaben in Notizen ("morgen 9 Uhr", "in 2 Stunden", "am Montag um 14:30", "tomorrow at 9 am",
/// "24.12. 10 Uhr"). Eine bloße Uhrzeit zählt nur mit "um"/"at" davor, damit "das Meeting war 14:00" keine
/// Erinnerung auslöst. Ohne Uhrzeit gilt 9:00; eine Uhrzeit ohne Tag meint heute bzw. morgen, wenn sie vorbei ist.
/// </summary>
public static partial class ReminderParser
{
    public static readonly TimeOnly DefaultTime = new(9, 0);

    private static readonly Dictionary<string, int> Numbers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ein"] = 1, ["eine"] = 1, ["einer"] = 1, ["einem"] = 1, ["einen"] = 1, ["zwei"] = 2, ["drei"] = 3, ["vier"] = 4,
        ["fünf"] = 5, ["sechs"] = 6, ["zehn"] = 10, ["zwanzig"] = 20, ["dreißig"] = 30, ["a"] = 1, ["an"] = 1, ["one"] = 1,
        ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["ten"] = 10, ["twenty"] = 20, ["thirty"] = 30,
        ["halben"] = 0, ["half"] = 0,
    };

    private static readonly Dictionary<string, DayOfWeek> Weekdays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["montag"] = DayOfWeek.Monday, ["dienstag"] = DayOfWeek.Tuesday, ["mittwoch"] = DayOfWeek.Wednesday,
        ["donnerstag"] = DayOfWeek.Thursday, ["freitag"] = DayOfWeek.Friday, ["samstag"] = DayOfWeek.Saturday,
        ["sonntag"] = DayOfWeek.Sunday, ["monday"] = DayOfWeek.Monday, ["tuesday"] = DayOfWeek.Tuesday,
        ["wednesday"] = DayOfWeek.Wednesday, ["thursday"] = DayOfWeek.Thursday, ["friday"] = DayOfWeek.Friday,
        ["saturday"] = DayOfWeek.Saturday, ["sunday"] = DayOfWeek.Sunday,
    };

    [GeneratedRegex(@"\b(?:in|nach|in\s+einer?)\s+(?<n>\d+|[a-zäöüß]+)\s+(?<u>minuten?|min|stunden?|std|tagen?|tag|minutes?|mins?|hours?|hrs?|days?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Relative();

    [GeneratedRegex(@"\b(?:in\s+einer?\s+halben\s+stunde|in\s+half\s+an\s+hour)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HalfHour();

    [GeneratedRegex(@"\b(?<w>übermorgen|morgen|heute|tomorrow|today|day\s+after\s+tomorrow|(?:am\s+|on\s+|next\s+|nächsten\s+)?(?:montag|dienstag|mittwoch|donnerstag|freitag|samstag|sonntag|monday|tuesday|wednesday|thursday|friday|saturday|sunday))\b", RegexOptions.IgnoreCase)]
    private static partial Regex DayWord();

    [GeneratedRegex(@"\b(?<d>\d{1,2})\.(?<m>\d{1,2})\.(?<y>\d{2,4})?", RegexOptions.IgnoreCase)]
    private static partial Regex Date();

    // 14:30 / 14.30 Uhr / 9 Uhr / 9 Uhr 30 / 9 am / 9:30 pm
    [GeneratedRegex(@"(?<pre>\b(?:um|ab|gegen|at|by)\s+)?\b(?<h>\d{1,2})(?:[:.](?<min>\d{2})(?!\d))?\s*(?<suf>uhr(?:\s+(?<min2>\d{1,2})\b)?|am\b|pm\b|a\.m\.|p\.m\.)?", RegexOptions.IgnoreCase)]
    private static partial Regex Time();

    public static ReminderMatch? Parse(string text, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (HalfHour().Match(text) is { Success: true } hh)
            return new ReminderMatch(now.AddMinutes(30), hh.Value);
        if (Relative().Match(text) is { Success: true } rel && Amount(rel.Groups["n"].Value) is { } n && n > 0)
        {
            var unit = rel.Groups["u"].Value.ToLowerInvariant();
            var due = unit.StartsWith("min") ? now.AddMinutes(n)
                : unit.StartsWith("st") || unit.StartsWith("h") ? now.AddHours(n)
                : now.Date.AddDays(n).Add(DefaultTime.ToTimeSpan());
            return new ReminderMatch(due, rel.Value);
        }

        DateOnly? day = null;
        var expr = new List<string>();
        var dw = DayWord().Match(text);
        // "heute Morgen" = heute früh, nicht "morgen"
        if (dw.Success && dw.Groups["w"].Value.Equals("morgen", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(text[..dw.Index], @"\bheute\s*$", RegexOptions.IgnoreCase))
            dw = Match.Empty;
        if (dw.Success)
        {
            day = ResolveDay(dw.Groups["w"].Value, DateOnly.FromDateTime(now));
            expr.Add(dw.Value);
        }
        else if (Date().Match(text) is { Success: true } dm &&
                 int.TryParse(dm.Groups["d"].Value, out var d) && int.TryParse(dm.Groups["m"].Value, out var m) && m is >= 1 and <= 12 && d is >= 1 and <= 31)
        {
            var y = dm.Groups["y"].Success && int.TryParse(dm.Groups["y"].Value, out var yy) ? (yy < 100 ? 2000 + yy : yy) : now.Year;
            if (d <= DateTime.DaysInMonth(y, m))
            {
                var date = new DateOnly(y, m, d);
                if (!dm.Groups["y"].Success && date < DateOnly.FromDateTime(now)) date = date.AddYears(1);
                day = date;
                expr.Add(dm.Value);
            }
        }

        TimeOnly? time = null;
        foreach (Match t in Time().Matches(text))
        {
            if (!int.TryParse(t.Groups["h"].Value, out var h)) continue;
            var hasPre = t.Groups["pre"].Success;
            var suf = t.Groups["suf"].Value.ToLowerInvariant();
            var hasMin = t.Groups["min"].Success;
            // Nur echte Uhrzeiten: "14:30", "9 Uhr", "9 am" – oder "um 9". Kein Datum (24.12.), keine Mengen.
            if (!hasMin && suf.Length == 0 && !hasPre) continue;
            if (hasMin && t.Groups["min"].Value.Length == 2 && text.Length > t.Index + t.Length && text[t.Index + t.Length] == '.') continue;
            if (!hasPre && suf.Length == 0 && day is null) continue; // bloßes "14:30" ohne Tag/um: keine Erinnerung
            var min = t.Groups["min"].Success ? int.Parse(t.Groups["min"].Value, CultureInfo.InvariantCulture)
                : t.Groups["min2"].Success ? int.Parse(t.Groups["min2"].Value, CultureInfo.InvariantCulture) : 0;
            if (suf.StartsWith('p') && h < 12) h += 12;
            if (suf.StartsWith('a') && h == 12) h = 0;
            if (h > 23 || min > 59) continue;
            time = new TimeOnly(h, min);
            expr.Add(t.Value.Trim());
            break;
        }

        if (day is null && time is null) return null;
        // "heute" ohne Uhrzeit ist zu vage ("heute war es kalt").
        if (time is null && dw.Success && dw.Groups["w"].Value.ToLowerInvariant() is "heute" or "today") return null;
        var dueDay = day ?? DateOnly.FromDateTime(now);
        var dueTime = time ?? DefaultTime;
        var result = dueDay.ToDateTime(dueTime);
        if (day is null && result <= now) result = result.AddDays(1);   // Uhrzeit heute schon vorbei → morgen
        return new ReminderMatch(result, string.Join(" ", expr));
    }

    private static int? Amount(string s)
    {
        if (int.TryParse(s, out var n)) return n;
        return Numbers.TryGetValue(s, out var w) ? w : null;
    }

    private static DateOnly ResolveDay(string word, DateOnly today)
    {
        var w = Regex.Replace(word.ToLowerInvariant(), @"^(am|on|next|nächsten)\s+", "");
        switch (w)
        {
            case "heute" or "today": return today;
            case "morgen" or "tomorrow": return today.AddDays(1);
            case "übermorgen": return today.AddDays(2);
        }
        if (w.StartsWith("day after")) return today.AddDays(2);
        if (Weekdays.TryGetValue(w, out var dow))
        {
            var diff = ((int)dow - (int)today.DayOfWeek + 7) % 7;
            return today.AddDays(diff == 0 ? 7 : diff);
        }
        return today;
    }
}
