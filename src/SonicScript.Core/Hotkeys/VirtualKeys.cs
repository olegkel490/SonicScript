namespace SonicScript.Core.Hotkeys;

/// <summary>Windows Virtual-Key-Codes als plattformneutrale Zahlen.</summary>
public static class VirtualKeys
{
    public const int Back = 0x08;
    public const int Tab = 0x09;
    public const int Enter = 0x0D;
    public const int Shift = 0x10;
    public const int Control = 0x11;
    public const int Menu = 0x12; // Alt
    public const int Pause = 0x13;
    public const int Escape = 0x1B;
    public const int Space = 0x20;
    public const int LWin = 0x5B;
    public const int RWin = 0x5C;
    public const int LShift = 0xA0;
    public const int RShift = 0xA1;
    public const int LControl = 0xA2;
    public const int RControl = 0xA3;
    public const int LMenu = 0xA4;
    public const int RMenu = 0xA5;
    public const int F1 = 0x70;
    public const int OemPlus = 0xBB;
    public const int OemComma = 0xBC;
    public const int OemMinus = 0xBD;
    public const int OemPeriod = 0xBE;
    public const int Oem2 = 0xBF; // DE: #'


    /// <summary>Unbelegter VK-Code; wird nach Win-Kombis injiziert, damit das Startmenü nicht aufgeht.</summary>
    public const int MenuMask = 0xE8;

    private static readonly Dictionary<string, int> ByName = BuildNames();
    private static readonly Dictionary<int, string> ByCode = ByName
        .GroupBy(kv => kv.Value)
        .ToDictionary(g => g.Key, g => g.First().Key);

    private static Dictionary<string, int> BuildNames()
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Space"] = Space, ["Leertaste"] = Space,
            ["Enter"] = Enter, ["Tab"] = Tab, ["Backspace"] = Back,
            ["Esc"] = Escape, ["Escape"] = Escape,
            ["Pause"] = Pause, ["ScrollLock"] = 0x91,
            ["Insert"] = 0x2D, ["Einfg"] = 0x2D,
            ["Delete"] = 0x2E, ["Entf"] = 0x2E,
            ["Home"] = 0x24, ["Pos1"] = 0x24, ["End"] = 0x23, ["Ende"] = 0x23,
            ["PageUp"] = 0x21, ["PageDown"] = 0x22,
            ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
            ["Apps"] = 0x5D,
            // Satzzeichen-Tasten (Position auf deutscher und US-Tastatur gleich)
            [","] = OemComma, ["Komma"] = OemComma, ["Comma"] = OemComma,
            ["."] = OemPeriod, ["Punkt"] = OemPeriod, ["Period"] = OemPeriod,
            ["-"] = OemMinus, ["Minus"] = OemMinus,
            ["#"] = Oem2, ["Plus"] = OemPlus,
        };
        for (var i = 0; i < 26; i++) d[((char)('A' + i)).ToString()] = 0x41 + i;
        for (var i = 0; i < 10; i++) d[i.ToString()] = 0x30 + i;
        for (var i = 0; i < 10; i++) d["NumPad" + i] = 0x60 + i;
        for (var i = 1; i <= 24; i++) d["F" + i] = F1 + i - 1;
        return d;
    }

    public static bool TryParse(string name, out int vk)
    {
        var n = name.Trim();
        if (ByName.TryGetValue(n, out vk)) return true;
        // Sonstige Tasten als Hex-Code ("0xBA"), wie Name() sie ausgibt.
        if (n.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(n[2..], System.Globalization.NumberStyles.HexNumber, null, out vk) && vk is > 0 and < 0xFF && !IsModifier(vk))
            return true;
        vk = 0;
        return false;
    }

    public static string Name(int vk) => ByCode.TryGetValue(vk, out var n) ? n : $"0x{vk:X2}";

    public static bool IsModifier(int vk) => ModifierOf(vk) != KeyModifiers.None;

    public static KeyModifiers ModifierOf(int vk) => vk switch
    {
        Shift or LShift or RShift => KeyModifiers.Shift,
        Control or LControl or RControl => KeyModifiers.Ctrl,
        Menu or LMenu or RMenu => KeyModifiers.Alt,
        LWin or RWin => KeyModifiers.Win,
        _ => KeyModifiers.None,
    };
}
