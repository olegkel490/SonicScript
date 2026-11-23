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
        t = t.Replace(PromptBuilder.TranscriptOpen, "").Replace(PromptBuilder.TranscriptClose, "")
             .Replace(PromptBuilder.TextOpen, "").Replace(PromptBuilder.TextClose, "")
             .Replace(PromptBuilder.MailOpen, "").Replace(PromptBuilder.MailClose, "").Trim();
        t = Preamble().Replace(t, "").Trim();
        if (t.Length >= 2 && ((t[0] == '"' && t[^1] == '"') || (t[0] == '„' && t[^1] == '“') || (t[0] == '«' && t[^1] == '»')))
            t = t[1..^1].Trim();
        return t;
    }

    public static RewriteVerdict Check(string input, string cleanedOutput, ModeKind kind)
    {
        if (string.IsNullOrWhiteSpace(cleanedOutput)) return RewriteVerdict.Empty;
        // Prompts dürfen strukturierter (länger) werden als das Diktat.
        // E-Mails bekommen Anrede, Absätze und Gruß – aus kurzen Diktaten werden deutlich längere Texte.
        var limit = kind is ModeKind.Mail or ModeKind.MailEnglish ? input.Length * 6 + 600 : input.Length * 3 + 300;
        if (kind is not (ModeKind.Custom or ModeKind.Prompt or ModeKind.Reply) && cleanedOutput.Length > limit) return RewriteVerdict.TooLong;
        // Bei Übersetzungen ist "I can't …" oft eine korrekte Übersetzung, keine Verweigerung.
        if (kind is not (ModeKind.Custom or ModeKind.Translate or ModeKind.Prompt or ModeKind.MailEnglish or ModeKind.Reply) && !Refusal().IsMatch(input.TrimStart()) && Refusal().IsMatch(cleanedOutput.TrimStart()))
            return RewriteVerdict.LooksLikeAnswer;
        return RewriteVerdict.Ok;
    }
}
