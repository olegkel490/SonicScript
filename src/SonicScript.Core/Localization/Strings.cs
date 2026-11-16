using System.Globalization;

namespace SonicScript.Core.Localization;

/// <summary>UI-Texte Deutsch/Englisch. Fehlender Schlüssel → deutscher Text → Schlüssel selbst.</summary>
public static partial class Strings
{
    public static string Language { get; private set; } = "de";

    /// <summary>"auto" = Systemsprache (Deutsch bei de-*, sonst Englisch).</summary>
    public static void SetLanguage(string? uiLanguage)
    {
        var lang = uiLanguage is null or "auto" ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : uiLanguage;
        Language = lang == "de" ? "de" : "en";
    }

    public static string Get(string key)
    {
        var table = Language == "de" ? De : En;
        return table.TryGetValue(key, out var v) ? v : De.TryGetValue(key, out var d) ? d : key;
    }

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>Text für Enum-Werte: Schlüssel "enum.{Typ}.{Wert}".</summary>
    public static string ForEnum(Enum value) => Get($"enum.{value.GetType().Name}.{value}");

    internal static IReadOnlyDictionary<string, string> German => De;
    internal static IReadOnlyDictionary<string, string> English => En;
}
