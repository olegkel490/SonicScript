namespace SonicScript.Core.Hotkeys;

public enum TriggerMode
{
    /// <summary>Halten = aufnehmen, loslassen = fertig.</summary>
    Hold,
    /// <summary>Einmal drücken = Start, nochmal drücken = Stopp.</summary>
    Toggle,
}

public enum HotkeyAction
{
    /// <summary>Diktat; <see cref="HotkeyBinding.ModeId"/> = null heißt: Modus über App-Regel.</summary>
    Dictate,
    /// <summary>Blitznotiz in die Markdown-Ablage.</summary>
    Note,
    /// <summary>Notiz-Bibliothek ein-/ausblenden.</summary>
    ToggleLibrary,
    /// <summary>Markierten Text per gesprochener Anweisung bearbeiten.</summary>
    EditSelection,
    /// <summary>Nächsten Modus wählen.</summary>
    NextMode,
    /// <summary>Vorherigen Modus wählen.</summary>
    PreviousMode,
    /// <summary>Letztes Diktat rückgängig: erst wortgetreue Fassung, dann ganz entfernen.</summary>
    Undo,
}

public sealed record HotkeyBinding(string Id, KeyCombo Combo, TriggerMode Trigger, HotkeyAction Action, string? ModeId = null);

public enum HotkeyEventKind
{
    Start,
    Stop,
    /// <summary>Während einer Halten-Aufnahme wurde ein Modifikator ergänzt → anderer Modus.</summary>
    Switch,
    Cancel,
    ToggleLibrary,
    NextMode,
    PreviousMode,
    Undo,
}

public readonly record struct HotkeyEvent(HotkeyEventKind Kind, HotkeyBinding? Binding);

/// <param name="Replay">
/// Kurz angetippte Halten-Kombi mit Haupttaste (z. B. Strg+-): Aufnahme verworfen, die Kombination soll
/// an die Ziel-App nachgereicht werden, damit deren normale Funktion (z. B. Herauszoomen) erhalten bleibt.
/// </param>
public readonly record struct KeyResult(bool Swallow, HotkeyEvent? Event, bool InjectMenuMask, KeyCombo? Replay = null)
{
    public static readonly KeyResult PassThrough = new(false, null, false);
}

/// <summary>
/// Reine Logik hinter dem globalen Tastatur-Hook. Bekommt jede physische Taste (down/up) und entscheidet,
/// ob eine Aufnahme startet/stoppt/abbricht und ob die Taste verschluckt wird.
/// Modifikatoren werden nie verschluckt; Haupttasten nur, wenn sie exakt zu einer Belegung passen.
/// </summary>
public sealed class HotkeyMatcher
{
    /// <summary>Andere Taste innerhalb dieser Zeit nach Start einer Nur-Modifikator-Kombi = Nutzer wollte ein Systemkürzel.</summary>
    public static readonly TimeSpan ShortcutGrace = TimeSpan.FromMilliseconds(600);

    /// <summary>Kürzer gehaltene Kombis mit Haupttaste gelten als normales Tippen (Antippen = Originalfunktion).</summary>
    public static readonly TimeSpan TapThreshold = TimeSpan.FromMilliseconds(350);

    private readonly IReadOnlyList<HotkeyBinding> _bindings;
    private readonly int _cancelKey;
    private readonly TimeProvider _time;
    private readonly HashSet<int> _held = [];
    private readonly HashSet<int> _swallowedDowns = [];
    private long _sessionStart;
    private bool _winConsumed;

    public HotkeyMatcher(IEnumerable<HotkeyBinding> bindings, int cancelKey = VirtualKeys.Escape, TimeProvider? time = null)
    {
        _bindings = bindings.Where(b => b.Combo.IsValid).ToList();
        _cancelKey = cancelKey;
        _time = time ?? TimeProvider.System;
    }

    public HotkeyBinding? ActiveBinding { get; private set; }
    public bool IsSessionActive => ActiveBinding is not null;

    /// <summary>Von außen beenden (z. B. Klick auf den Aufnahme-Indikator oder Fehler).</summary>
    public void EndSession() => ActiveBinding = null;

    public KeyModifiers CurrentModifiers
    {
        get
        {
            var m = KeyModifiers.None;
            foreach (var vk in _held) m |= VirtualKeys.ModifierOf(vk);
            return m;
        }
    }

    private bool NonModifierHeld(int except) => _held.Any(k => k != except && !VirtualKeys.IsModifier(k));

    public KeyResult OnKey(int vk, bool down) => down ? OnDown(vk) : OnUp(vk);

    private KeyResult OnDown(int vk)
    {
        if (!_held.Add(vk))
        {
            // Auto-Repeat: Haupttaste einer aktiven Belegung weiter verschlucken.
            return new KeyResult(_swallowedDowns.Contains(vk), null, false);
        }

        var mods = CurrentModifiers;
        var isMod = VirtualKeys.IsModifier(vk);

        if (ActiveBinding is { } active)
        {
            if (vk == _cancelKey)
            {
                ActiveBinding = null;
                return Swallowed(vk, new HotkeyEvent(HotkeyEventKind.Cancel, active));
            }

            if (active.Trigger == TriggerMode.Toggle)
            {
                if (Matches(active.Combo, vk, mods, isMod))
                {
                    ActiveBinding = null;
                    return active.Combo.IsModifierOnly
                        ? new KeyResult(false, new HotkeyEvent(HotkeyEventKind.Stop, active), false)
                        : Swallowed(vk, new HotkeyEvent(HotkeyEventKind.Stop, active));
                }
                return KeyResult.PassThrough;
            }

            // Halten
            if (active.Combo.IsModifierOnly)
            {
                if (isMod)
                {
                    var upgrade = _bindings.FirstOrDefault(b =>
                        b != active && !IsCommand(b.Action) && b.Trigger == TriggerMode.Hold &&
                        b.Combo.IsModifierOnly && b.Combo.Modifiers == mods);
                    if (upgrade is not null)
                    {
                        ActiveBinding = upgrade;
                        if (mods.HasFlag(KeyModifiers.Win)) _winConsumed = true;
                        return new KeyResult(false, new HotkeyEvent(HotkeyEventKind.Switch, upgrade), false);
                    }
                    return KeyResult.PassThrough;
                }

                if (_time.GetElapsedTime(_sessionStart) < ShortcutGrace)
                {
                    // Ctrl+Win+D o. Ä.: der Nutzer wollte ein Systemkürzel, keine Aufnahme.
                    ActiveBinding = null;
                    return new KeyResult(false, new HotkeyEvent(HotkeyEventKind.Cancel, active), false);
                }
            }
            return KeyResult.PassThrough;
        }

        // Keine Sitzung: passende Belegung suchen.
        var match = _bindings.FirstOrDefault(b => Matches(b.Combo, vk, mods, isMod));
        if (match is null) return KeyResult.PassThrough;

        HotkeyEvent ev;
        if (IsCommand(match.Action))
        {
            ev = new HotkeyEvent(match.Action switch
            {
                HotkeyAction.NextMode => HotkeyEventKind.NextMode,
                HotkeyAction.PreviousMode => HotkeyEventKind.PreviousMode,
                HotkeyAction.Undo => HotkeyEventKind.Undo,
                _ => HotkeyEventKind.ToggleLibrary,
            }, match);
        }
        else
        {
            ActiveBinding = match;
            _sessionStart = _time.GetTimestamp();
            ev = new HotkeyEvent(HotkeyEventKind.Start, match);
        }
        if (match.Combo.Modifiers.HasFlag(KeyModifiers.Win)) _winConsumed = true;

        return match.Combo.IsModifierOnly ? new KeyResult(false, ev, false) : Swallowed(vk, ev);
    }

    /// <summary>Einmalige Befehle ohne Aufnahme-Sitzung.</summary>
    private static bool IsCommand(HotkeyAction a) => a is HotkeyAction.ToggleLibrary or HotkeyAction.NextMode or HotkeyAction.PreviousMode or HotkeyAction.Undo;

    private KeyResult OnUp(int vk)
    {
        _held.Remove(vk);
        var swallow = _swallowedDowns.Remove(vk);
        HotkeyEvent? ev = null;
        KeyCombo? replay = null;

        if (ActiveBinding is { Trigger: TriggerMode.Hold } active && IsPartOf(active.Combo, vk))
        {
            ActiveBinding = null;
            if (!active.Combo.IsModifierOnly && _time.GetElapsedTime(_sessionStart) < TapThreshold)
            {
                ev = new HotkeyEvent(HotkeyEventKind.Cancel, active);
                replay = active.Combo;
            }
            else
            {
                ev = new HotkeyEvent(HotkeyEventKind.Stop, active);
            }
        }

        var injectMask = false;
        if (VirtualKeys.ModifierOf(vk) == KeyModifiers.Win && !CurrentModifiers.HasFlag(KeyModifiers.Win))
        {
            injectMask = _winConsumed;
            _winConsumed = false;
        }

        return new KeyResult(swallow, ev, injectMask, replay);
    }

    private bool IsPartOf(KeyCombo combo, int vk)
    {
        if (combo.Key == vk) return true;
        var mod = VirtualKeys.ModifierOf(vk);
        // Nur stoppen, wenn der Modifikator wirklich weg ist (z. B. nicht, wenn noch die rechte Strg-Taste gehalten wird).
        return mod != KeyModifiers.None && combo.Modifiers.HasFlag(mod) && !CurrentModifiers.HasFlag(mod);
    }

    private bool Matches(KeyCombo combo, int vk, KeyModifiers mods, bool isMod)
    {
        if (combo.IsModifierOnly)
            return isMod && mods == combo.Modifiers && !NonModifierHeld(-1);
        return vk == combo.Key && mods == combo.Modifiers;
    }

    private KeyResult Swallowed(int vk, HotkeyEvent ev)
    {
        _swallowedDowns.Add(vk);
        return new KeyResult(true, ev, false);
    }
}
