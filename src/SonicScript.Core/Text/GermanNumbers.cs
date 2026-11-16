namespace SonicScript.Core.Text;

/// <summary>Deutsche Zahlwörter 0–59 (für Uhrzeiten).</summary>
public static class GermanNumbers
{
    private static readonly Dictionary<string, int> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["null"] = 0, ["ein"] = 1, ["eins"] = 1, ["eine"] = 1, ["zwei"] = 2, ["zwo"] = 2, ["drei"] = 3, ["vier"] = 4,
        ["fünf"] = 5, ["sechs"] = 6, ["sieben"] = 7, ["acht"] = 8, ["neun"] = 9, ["zehn"] = 10,
        ["elf"] = 11, ["zwölf"] = 12, ["dreizehn"] = 13, ["vierzehn"] = 14, ["fünfzehn"] = 15,
        ["sechzehn"] = 16, ["siebzehn"] = 17, ["achtzehn"] = 18, ["neunzehn"] = 19,
    };

    private static readonly Dictionary<string, int> Tens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zwanzig"] = 20, ["dreißig"] = 30, ["dreissig"] = 30, ["vierzig"] = 40, ["fünfzig"] = 50,
    };

    /// <summary>Regex-Fragment, das alle unterstützten Zahlwörter matcht (längste zuerst).</summary>
    public static readonly string Pattern = BuildPattern();

    private static string BuildPattern()
    {
        var ones = "(?:ein|zwei|drei|vier|fünf|sechs|sieben|acht|neun)";
        var tens = "(?:zwanzig|dreißig|dreissig|vierzig|fünfzig)";
        var units = string.Join('|', Units.Keys.OrderByDescending(k => k.Length));
        return $"(?:{ones}und{tens}|{tens}|{units})";
    }

    public static int? Parse(string word)
    {
        var w = word.Trim();
        if (int.TryParse(w, out var n)) return n;
        if (Units.TryGetValue(w, out n)) return n;
        if (Tens.TryGetValue(w, out n)) return n;
        var und = w.IndexOf("und", StringComparison.OrdinalIgnoreCase);
        if (und > 0 && Units.TryGetValue(w[..und], out var one) && one is >= 1 and <= 9 &&
            Tens.TryGetValue(w[(und + 3)..], out var ten))
            return ten + one;
        return null;
    }
}
