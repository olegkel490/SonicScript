namespace SonicScript.Core.Hotkeys;

public enum ConflictKind
{
    Invalid,
    Duplicate,
    ReservedBySystem,
}

public sealed record HotkeyConflict(ConflictKind Kind, string BindingId, string? OtherBindingId, KeyCombo Combo);

public static class HotkeyConflicts
{
    /// <summary>Bekannte Windows-Kürzel, die nicht überschrieben werden sollten.</summary>
    private static readonly KeyCombo[] Reserved =
    [
        new(KeyModifiers.Win, 'L'),
        new(KeyModifiers.Win, 'D'),
        new(KeyModifiers.Win, 'E'),
        new(KeyModifiers.Win, 'R'),
        new(KeyModifiers.Win, 'V'),
        new(KeyModifiers.Win, VirtualKeys.Tab),
        new(KeyModifiers.Alt, VirtualKeys.Tab),
        new(KeyModifiers.Alt, VirtualKeys.F1 + 3), // Alt+F4
        new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x2E), // Ctrl+Alt+Entf
        new(KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeys.Escape),
        new(KeyModifiers.Ctrl, 'C'),
        new(KeyModifiers.Ctrl, 'V'),
        new(KeyModifiers.Ctrl, 'X'),
        new(KeyModifiers.Ctrl, 'Z'),
        new(KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Win, 0), // Office-Taste
    ];

    public static IReadOnlyList<HotkeyConflict> Find(IEnumerable<HotkeyBinding> bindings)
    {
        var result = new List<HotkeyConflict>();
        var seen = new Dictionary<KeyCombo, string>();
        foreach (var b in bindings)
        {
            if (b.Combo.IsEmpty) continue; // nicht belegt ist erlaubt
            if (!b.Combo.IsValid)
            {
                result.Add(new HotkeyConflict(ConflictKind.Invalid, b.Id, null, b.Combo));
                continue;
            }
            if (seen.TryGetValue(b.Combo, out var other))
                result.Add(new HotkeyConflict(ConflictKind.Duplicate, b.Id, other, b.Combo));
            else
                seen[b.Combo] = b.Id;

            if (Reserved.Contains(b.Combo))
                result.Add(new HotkeyConflict(ConflictKind.ReservedBySystem, b.Id, null, b.Combo));
        }
        return result;
    }
}
