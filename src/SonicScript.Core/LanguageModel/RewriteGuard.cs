using System.Text.RegularExpressions;
using SonicScript.Core.Modes;

namespace SonicScript.Core.LanguageModel;

public enum RewriteVerdict
{
    Ok,
    Empty,
    TooLong,
    LooksLikeAnswer,
}

/// <summary>
/// Prüft die Antwort des Sprachmodells: Einleitungen/Tags/Anführungszeichen entfernen und Antworten verwerfen,
/// die offensichtlich keine Umformulierung sind (leer, viel zu lang, Rückfrage statt Text).
/// </summary>
public static partial class RewriteGuard
{
    [GeneratedRegex(@"^\s*(?:(?:hier ist|hier sind|here is|here's|sure|gerne|natürlich|klar|okay|ok)\b[^\n]*:\s*\n+)", RegexOptions.IgnoreCase)]
    private static partial Regex Preamble();

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlock();

    [GeneratedRegex(@"^```[a-zA-Z]*\s*\n(.*)\n```\s*$", RegexOptions.Singleline)]
    private static partial Regex CodeFence();

    [GeneratedRegex(@"^(?:ich kann|ich habe keinen|als ki|i can't|i cannot|as an ai|könnten sie|could you)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Refusal();

    public static string Clean(string output)
    {
        var t = ThinkBlock().Replace(output ?? "", "").Trim();
        var fence = CodeFence().Match(t);
        if (fence.Success) t = fence.Groups[1].Value.Trim();
        t = t.Replace(PromptBuilder.TranscriptOpen, "").Replace(PromptBuilder.TranscriptClose, "").Trim();
        t = Preamble().Replace(t, "").Trim();
        if (t.Length >= 2 && ((t[0] == '"' && t[^1] == '"') || (t[0] == '„' && t[^1] == '“') || (t[0] == '«' && t[^1] == '»')))
            t = t[1..^1].Trim();
        return t;
    }

    public static RewriteVerdict Check(string input, string cleanedOutput, ModeKind kind)
    {
        if (string.IsNullOrWhiteSpace(cleanedOutput)) return RewriteVerdict.Empty;
        if (kind != ModeKind.Custom && cleanedOutput.Length > input.Length * 3 + 300) return RewriteVerdict.TooLong;
        if (kind != ModeKind.Custom && !Refusal().IsMatch(input.TrimStart()) && Refusal().IsMatch(cleanedOutput.TrimStart()))
            return RewriteVerdict.LooksLikeAnswer;
        return RewriteVerdict.Ok;
    }
}
