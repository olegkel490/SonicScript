using SonicScript.Core.Settings;

namespace SonicScript.Core.Rules;

public sealed record ForegroundApp(string ProcessName, string WindowTitle);

/// <summary>Modus und Einfügeart pro App oder Webseite (Fenstertitel). Erste passende Regel gewinnt.</summary>
public static class AppRuleResolver
{
    public static AppRule? Match(IEnumerable<AppRule> rules, ForegroundApp? app)
    {
        if (app is null) return null;
        var proc = NormalizeProcess(app.ProcessName);
        foreach (var r in rules)
        {
            var rp = NormalizeProcess(r.ProcessName);
            if (rp.Length == 0 && string.IsNullOrWhiteSpace(r.TitleContains)) continue; // leere Regel
            if (rp.Length > 0 && !string.Equals(rp, proc, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrWhiteSpace(r.TitleContains) &&
                app.WindowTitle.IndexOf(r.TitleContains.Trim(), StringComparison.OrdinalIgnoreCase) < 0) continue;
            return r;
        }
        return null;
    }

    public static string ResolveModeId(AppSettings s, ForegroundApp? app)
    {
        var rule = Match(s.AppRules, app);
        return rule is not null && s.FindMode(rule.ModeId) is not null ? rule.ModeId : s.DefaultMode.Id;
    }

    public static InsertionMethod ResolveInsertion(AppSettings s, ForegroundApp? app) =>
        Match(s.AppRules.Where(r => r.Insertion is not null), app)?.Insertion ?? s.Insertion.Method;

    public static string NormalizeProcess(string? name)
    {
        var n = (name ?? "").Trim();
        return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n;
    }
}
