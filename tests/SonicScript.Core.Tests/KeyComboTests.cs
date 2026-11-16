using SonicScript.Core.Hotkeys;

namespace SonicScript.Core.Tests;

public class KeyComboTests
{
    [Theory]
    [InlineData("Ctrl+Win", KeyModifiers.Ctrl | KeyModifiers.Win, 0)]
    [InlineData("strg + alt + space", KeyModifiers.Ctrl | KeyModifiers.Alt, 0x20)]
    [InlineData("F9", KeyModifiers.None, 0x78)]
    [InlineData("Shift+Win+Ctrl", KeyModifiers.Ctrl | KeyModifiers.Win | KeyModifiers.Shift, 0)]
    [InlineData("Ctrl+Alt+L", KeyModifiers.Ctrl | KeyModifiers.Alt, 'L')]
    public void Parses(string text, KeyModifiers mods, int key)
    {
        Assert.True(KeyCombo.TryParse(text, out var c));
        Assert.Equal(new KeyCombo(mods, key), c);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl")]          // nur ein Modifikator
    [InlineData("Ctrl+A+B")]      // zwei Haupttasten
    [InlineData("Ctrl+Banana")]
    public void RejectsInvalid(string text) => Assert.False(KeyCombo.TryParse(text, out _));

    [Fact]
    public void RoundTripsCanonicalText()
    {
        var c = KeyCombo.Parse("win+shift+ctrl");
        Assert.Equal("Ctrl+Shift+Win", c.ToString());
        Assert.Equal(c, KeyCombo.Parse(c.ToString()));
    }

    [Fact]
    public void FindsDuplicatesAndReserved()
    {
        var bindings = new[]
        {
            new HotkeyBinding("a", KeyCombo.Parse("Ctrl+Win"), TriggerMode.Hold, HotkeyAction.Dictate),
            new HotkeyBinding("b", KeyCombo.Parse("Ctrl+Win"), TriggerMode.Hold, HotkeyAction.Dictate, "x"),
            new HotkeyBinding("c", KeyCombo.Parse("Win+L"), TriggerMode.Hold, HotkeyAction.Dictate, "y"),
            new HotkeyBinding("d", KeyCombo.None, TriggerMode.Hold, HotkeyAction.Dictate, "z"),
        };
        var conflicts = HotkeyConflicts.Find(bindings);
        Assert.Contains(conflicts, c => c.Kind == ConflictKind.Duplicate && c.BindingId == "b" && c.OtherBindingId == "a");
        Assert.Contains(conflicts, c => c.Kind == ConflictKind.ReservedBySystem && c.BindingId == "c");
        Assert.DoesNotContain(conflicts, c => c.BindingId == "d");
    }
}
