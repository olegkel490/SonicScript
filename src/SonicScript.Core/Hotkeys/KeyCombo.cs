using System.Text.Json.Serialization;

namespace SonicScript.Core.Hotkeys;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// Tastenkombination aus Modifikatoren und optional einer Haupttaste (Windows Virtual-Key-Code).
/// <see cref="Key"/> = 0 bedeutet "nur Modifikatoren" (z. B. Ctrl+Win).
/// </summary>
[JsonConverter(typeof(KeyComboJsonConverter))]
public readonly record struct KeyCombo(KeyModifiers Modifiers, int Key)
{
    public static readonly KeyCombo None = new(KeyModifiers.None, 0);

    public bool IsEmpty => Modifiers == KeyModifiers.None && Key == 0;
    public bool IsModifierOnly => Key == 0 && Modifiers != KeyModifiers.None;

    /// <summary>Gültig = nicht leer; reine Modifikator-Kombis brauchen mindestens zwei Modifikatoren.</summary>
    public bool IsValid
    {
        get
        {
            if (IsEmpty) return false;
            if (Key != 0) return !VirtualKeys.IsModifier(Key);
            return CountBits((int)Modifiers) >= 2;
        }
    }

    public static bool TryParse(string? text, out KeyCombo combo)
    {
        combo = None;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var mods = KeyModifiers.None;
        var key = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.ToLowerInvariant();
            KeyModifiers? mod = part switch
            {
                "ctrl" or "strg" or "control" => KeyModifiers.Ctrl,
                "alt" => KeyModifiers.Alt,
                "shift" or "umschalt" => KeyModifiers.Shift,
                "win" or "windows" or "meta" => KeyModifiers.Win,
                _ => null,
            };
            if (mod is { } m)
            {
                mods |= m;
                continue;
            }
            if (key != 0) return false; // nur eine Haupttaste
            if (!VirtualKeys.TryParse(raw, out key)) return false;
        }

        combo = new KeyCombo(mods, key);
        return combo.IsValid;
    }

    public static KeyCombo Parse(string text) =>
        TryParse(text, out var c) ? c : throw new FormatException($"Ungültige Tastenkombination: {text}");

    public override string ToString()
    {
        if (IsEmpty) return "";
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(KeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(KeyModifiers.Win)) parts.Add("Win");
        if (Key != 0) parts.Add(VirtualKeys.Name(Key));
        return string.Join('+', parts);
    }

    private static int CountBits(int v)
    {
        var n = 0;
        while (v != 0) { n += v & 1; v >>= 1; }
        return n;
    }
}

public sealed class KeyComboJsonConverter : JsonConverter<KeyCombo>
{
    public override KeyCombo Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        var s = reader.GetString();
        return KeyCombo.TryParse(s, out var c) ? c : KeyCombo.None;
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, KeyCombo value, System.Text.Json.JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
