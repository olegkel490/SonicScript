using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using SonicScript.Core.Hotkeys;
using SonicScript.Core.Localization;
using AKey = Avalonia.Input.Key;
using KeyModifiers = SonicScript.Core.Hotkeys.KeyModifiers;

namespace SonicScript.App.Controls;

/// <summary>Meldet, ob gerade eine Taste aufgenommen wird – dann pausiert der globale Hook.</summary>
public static class HotkeyCapture
{
    public static event Action<bool>? ActiveChanged;
    internal static void SetActive(bool active) => ActiveChanged?.Invoke(active);
}

/// <summary>
/// Eingabefeld für Tastenkombinationen: hineinklicken und die Kombination drücken.
/// Nur Modifikatoren (z. B. Strg+Win) werden beim Loslassen übernommen. Esc = abbrechen, Entf/Rücktaste = leeren.
/// </summary>
public sealed class HotkeyBox : TextBox
{
    public static readonly StyledProperty<KeyCombo> ComboProperty =
        AvaloniaProperty.Register<HotkeyBox, KeyCombo>(nameof(Combo), defaultBindingMode: BindingMode.TwoWay);

    private KeyModifiers _peak;
    private bool _mainKeySeen;

    public HotkeyBox()
    {
        IsReadOnly = true;
        CaretBrush = Avalonia.Media.Brushes.Transparent;
        Cursor = new Cursor(StandardCursorType.Hand);
        var clear = new Button { Content = "✕", Padding = new Thickness(6, 0), Background = Avalonia.Media.Brushes.Transparent, Focusable = false };
        ToolTip.SetTip(clear, Strings.Get("hotkey.clear"));
        clear.Click += (_, _) => Combo = KeyCombo.None;
        InnerRightContent = clear;
        Watermark = Strings.Get("hotkey.none");
        UpdateText();
    }

    protected override Type StyleKeyOverride => typeof(TextBox);

    public KeyCombo Combo
    {
        get => GetValue(ComboProperty);
        set => SetValue(ComboProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ComboProperty) UpdateText();
    }

    private void UpdateText() => Text = Display(Combo);

    public static string Display(KeyCombo c) =>
        c.IsEmpty ? "" : c.ToString().Replace("Ctrl", Strings.Language == "de" ? "Strg" : "Ctrl");

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        _peak = KeyModifiers.None;
        _mainKeySeen = false;
        Text = Strings.Get("hotkey.press");
        HotkeyCapture.SetActive(true);
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        HotkeyCapture.SetActive(false);
        UpdateText();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var mods = ToMods(e.KeyModifiers) | ModOf(e.Key);
        if (ModOf(e.Key) != KeyModifiers.None)
        {
            _peak |= mods;
            Text = Display(new KeyCombo(_peak, 0)) + "+…";
            return;
        }

        if (mods == KeyModifiers.None && e.Key == AKey.Escape) { Finish(); return; }
        if (mods == KeyModifiers.None && e.Key is AKey.Back or AKey.Delete) { Combo = KeyCombo.None; Finish(); return; }

        _mainKeySeen = true;
        var vk = ToVirtualKey(e.Key);
        if (vk == 0) return;
        var combo = new KeyCombo(mods, vk);
        if (!combo.IsValid) return;
        Combo = combo;
        Finish();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        e.Handled = true;
        if (ModOf(e.Key) == KeyModifiers.None) return;
        var remaining = ToMods(e.KeyModifiers) & ~ModOf(e.Key);
        if (remaining != KeyModifiers.None) return;
        // Alle Modifikatoren losgelassen, ohne Haupttaste → Nur-Modifikator-Kombi (mind. zwei).
        if (!_mainKeySeen && new KeyCombo(_peak, 0).IsValid)
        {
            Combo = new KeyCombo(_peak, 0);
            Finish();
            return;
        }
        _peak = KeyModifiers.None;
        Text = Strings.Get("hotkey.press");
    }

    private void Finish()
    {
        _peak = KeyModifiers.None;
        _mainKeySeen = false;
        TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
        UpdateText();
    }

    private static KeyModifiers ToMods(Avalonia.Input.KeyModifiers m)
    {
        var r = KeyModifiers.None;
        if (m.HasFlag(Avalonia.Input.KeyModifiers.Control)) r |= KeyModifiers.Ctrl;
        if (m.HasFlag(Avalonia.Input.KeyModifiers.Alt)) r |= KeyModifiers.Alt;
        if (m.HasFlag(Avalonia.Input.KeyModifiers.Shift)) r |= KeyModifiers.Shift;
        if (m.HasFlag(Avalonia.Input.KeyModifiers.Meta)) r |= KeyModifiers.Win;
        return r;
    }

    private static KeyModifiers ModOf(AKey k) => k switch
    {
        AKey.LeftCtrl or AKey.RightCtrl => KeyModifiers.Ctrl,
        AKey.LeftAlt or AKey.RightAlt => KeyModifiers.Alt,
        AKey.LeftShift or AKey.RightShift => KeyModifiers.Shift,
        AKey.LWin or AKey.RWin => KeyModifiers.Win,
        _ => KeyModifiers.None,
    };

    /// <summary>Avalonia-Taste → Windows Virtual-Key-Code.</summary>
    public static int ToVirtualKey(AKey k)
    {
        if (k >= AKey.A && k <= AKey.Z) return 'A' + (k - AKey.A);
        if (k >= AKey.D0 && k <= AKey.D9) return '0' + (k - AKey.D0);
        if (k >= AKey.NumPad0 && k <= AKey.NumPad9) return 0x60 + (k - AKey.NumPad0);
        if (k >= AKey.F1 && k <= AKey.F24) return VirtualKeys.F1 + (k - AKey.F1);
        return k switch
        {
            AKey.Space => VirtualKeys.Space,
            AKey.Enter => VirtualKeys.Enter,
            AKey.Tab => VirtualKeys.Tab,
            AKey.Insert => 0x2D,
            AKey.Home => 0x24,
            AKey.End => 0x23,
            AKey.PageUp => 0x21,
            AKey.PageDown => 0x22,
            AKey.Left => 0x25,
            AKey.Up => 0x26,
            AKey.Right => 0x27,
            AKey.Down => 0x28,
            AKey.Pause => VirtualKeys.Pause,
            AKey.Scroll => 0x91,
            AKey.Apps => 0x5D,
            AKey.OemComma => VirtualKeys.OemComma,
            AKey.OemPeriod => VirtualKeys.OemPeriod,
            AKey.OemMinus => VirtualKeys.OemMinus,
            AKey.OemPlus => VirtualKeys.OemPlus,
            AKey.OemQuestion => VirtualKeys.Oem2,
            AKey.OemSemicolon => 0xBA,
            AKey.OemTilde => 0xC0,
            AKey.OemOpenBrackets => 0xDB,
            AKey.OemPipe => 0xDC,
            AKey.OemCloseBrackets => 0xDD,
            AKey.OemQuotes => 0xDE,
            AKey.OemBackslash => 0xE2,
            _ => 0,
        };
    }
}
