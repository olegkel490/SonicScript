namespace SonicScript.Core.Text;

/// <summary>
/// Passt eingefügten Text an den Text vor dem Cursor an: Leerzeichen davor, Großschreibung am Satzanfang,
/// Kleinschreibung von Funktionswörtern mitten im Satz ("…, Und dann" → "…, und dann").
/// Substantive bleiben unangetastet (deutsche Großschreibung).
/// </summary>
public static class InsertionFitter
{
    private static readonly HashSet<string> FunctionWords = new(StringComparer.Ordinal)
    {
        // Deutsch
        "Und", "Oder", "Aber", "Dann", "Denn", "Doch", "Sondern", "Also", "Sowie", "Weil", "Wenn", "Dass", "Ob",
        "Als", "Wie", "Wo", "Was", "Wer", "Ich", "Du", "Er", "Es", "Wir", "Ihr", "Mit", "Ohne", "Für", "Von", "Zu",
        "Zum", "Zur", "Im", "In", "Auf", "Aus", "Bei", "Nach", "Vor", "Über", "Unter", "Um", "An", "Am", "Der", "Die",
        "Das", "Den", "Dem", "Des", "Ein", "Eine", "Einen", "Einem", "Einer", "Ist", "Sind", "War", "Hat", "Habe",
        "Nicht", "Auch", "Noch", "Schon", "Nur", "So", "Da", "Hier", "Jetzt", "Bitte", "Danke", "Mal", "Ja", "Nein",
        // Englisch ("I" bleibt groß)
        "And", "Or", "But", "Then", "So", "Because", "If", "That", "The", "A", "An", "To", "Of", "For", "With",
        "In", "On", "At", "By", "From", "It", "We", "You", "He", "She", "They", "Is", "Are", "Was", "Not", "Also",
        "Just", "Please", "Thanks", "Yes", "No",
    };

    private const string NoSpaceAfter = "([{„“\"'«‹/\\-–—";
    private const string NoSpaceBefore = ",.;:!?)]}»›…%";

    /// <param name="before">Text direkt vor dem Cursor (null = unbekannt → nichts ändern).</param>
    public static string Fit(string text, string? before)
    {
        if (string.IsNullOrEmpty(text) || before is null) return text;

        var t = text;
        var trimmed = before.TrimEnd(' ', '\t');
        var atSentenceStart = trimmed.Length == 0 || before.EndsWith('\n') || before.EndsWith('\r') ||
                              ".!?:".Contains(trimmed[^1]);

        t = atSentenceStart ? UpperFirst(t) : LowerFunctionWord(t);

        if (before.Length > 0 && !char.IsWhiteSpace(before[^1]) && !NoSpaceAfter.Contains(before[^1]) &&
            !char.IsWhiteSpace(t[0]) && !NoSpaceBefore.Contains(t[0]))
            t = " " + t;

        return t;
    }

    private static string UpperFirst(string t)
    {
        var i = FirstLetter(t);
        return i < 0 || char.IsUpper(t[i]) ? t : t[..i] + char.ToUpper(t[i]) + t[(i + 1)..];
    }

    private static string LowerFunctionWord(string t)
    {
        var i = FirstLetter(t);
        if (i < 0) return t;
        var end = i;
        while (end < t.Length && char.IsLetter(t[end])) end++;
        var word = t[i..end];
        return FunctionWords.Contains(word) ? t[..i] + char.ToLower(t[i]) + t[(i + 1)..] : t;
    }

    private static int FirstLetter(string t)
    {
        for (var i = 0; i < t.Length; i++)
        {
            if (char.IsLetter(t[i])) return i;
            if (!char.IsWhiteSpace(t[i]) && !"\"„“'«(".Contains(t[i])) return -1; // beginnt mit Zahl/Zeichen
        }
        return -1;
    }
}
