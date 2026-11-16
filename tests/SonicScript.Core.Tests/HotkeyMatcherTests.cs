using SonicScript.Core.Hotkeys;
using static SonicScript.Core.Hotkeys.VirtualKeys;

namespace SonicScript.Core.Tests;

public class HotkeyMatcherTests
{
    private static readonly HotkeyBinding Dictate = new("dictate", KeyCombo.Parse("Ctrl+Win"), TriggerMode.Hold, HotkeyAction.Dictate);
    private static readonly HotkeyBinding Improve = new("improve", KeyCombo.Parse("Ctrl+Shift+Win"), TriggerMode.Hold, HotkeyAction.Dictate, "improve");
    private static readonly HotkeyBinding Note = new("note", KeyCombo.Parse("Ctrl+Alt+Space"), TriggerMode.Hold, HotkeyAction.Note);
    private static readonly HotkeyBinding ToggleF9 = new("f9", KeyCombo.Parse("F9"), TriggerMode.Toggle, HotkeyAction.Dictate, "verbatim");
    private static readonly HotkeyBinding Library = new("lib", KeyCombo.Parse("Ctrl+Alt+L"), TriggerMode.Toggle, HotkeyAction.ToggleLibrary);

    private readonly FakeTime _time = new(DateTimeOffset.UnixEpoch);
    private HotkeyMatcher Create() => new([Dictate, Improve, Note, ToggleF9, Library], time: _time);

    [Fact]
    public void ModifierOnlyHold_StartsAndStopsWithoutSwallowing()
    {
        var m = Create();
        Assert.Null(m.OnKey(LControl, true).Event);
        var start = m.OnKey(LWin, true);
        Assert.Equal(HotkeyEventKind.Start, start.Event?.Kind);
        Assert.Same(Dictate, start.Event?.Binding);
        Assert.False(start.Swallow);

        _time.Advance(TimeSpan.FromSeconds(2));
        var stop = m.OnKey(LControl, false);
        Assert.Equal(HotkeyEventKind.Stop, stop.Event?.Kind);
        Assert.False(m.IsSessionActive);
    }

    [Fact]
    public void AddingShiftSwitchesToImprove()
    {
        var m = Create();
        m.OnKey(LControl, true);
        m.OnKey(LWin, true);
        var sw = m.OnKey(LShift, true);
        Assert.Equal(HotkeyEventKind.Switch, sw.Event?.Kind);
        Assert.Same(Improve, m.ActiveBinding);

        var stop = m.OnKey(LShift, false);
        Assert.Equal(HotkeyEventKind.Stop, stop.Event?.Kind);
        Assert.Same(Improve, stop.Event?.Binding);
    }

    [Fact]
    public void OtherKeyRightAfterStart_CancelsBecauseItIsASystemShortcut()
    {
        var m = Create();
        m.OnKey(LControl, true);
        m.OnKey(LWin, true);
        var r = m.OnKey('D', true); // Ctrl+Win+D = neuer Desktop
        Assert.Equal(HotkeyEventKind.Cancel, r.Event?.Kind);
        Assert.False(r.Swallow);
        Assert.False(m.IsSessionActive);
    }

    [Fact]
    public void OtherKeyLater_IsIgnored()
    {
        var m = Create();
        m.OnKey(LControl, true);
        m.OnKey(LWin, true);
        _time.Advance(TimeSpan.FromSeconds(1));
        var r = m.OnKey('D', true);
        Assert.Null(r.Event);
        Assert.True(m.IsSessionActive);
    }

    [Fact]
    public void WinComboRelease_RequestsStartMenuMask()
    {
        var m = Create();
        m.OnKey(LControl, true);
        m.OnKey(LWin, true);
        _time.Advance(TimeSpan.FromSeconds(1));
        var up = m.OnKey(LWin, false);
        Assert.True(up.InjectMenuMask);
        Assert.False(m.OnKey(LControl, false).InjectMenuMask);
    }

    [Fact]
    public void PlainWinPress_DoesNotRequestMask()
    {
        var m = Create();
        m.OnKey(LWin, true);
        Assert.False(m.OnKey(LWin, false).InjectMenuMask);
    }

    [Fact]
    public void MainKeyCombo_SwallowsDownRepeatAndUp()
    {
        var m = Create();
        m.OnKey(LControl, true);
        m.OnKey(LMenu, true);
        var down = m.OnKey(Space, true);
        Assert.True(down.Swallow);
        Assert.Same(Note, down.Event?.Binding);
        Assert.True(m.OnKey(Space, true).Swallow); // Auto-Repeat
        _time.Advance(TimeSpan.FromSeconds(2));
        var up = m.OnKey(Space, false);
        Assert.True(up.Swallow);
        Assert.Equal(HotkeyEventKind.Stop, up.Event?.Kind);
        Assert.Null(up.Replay);
    }

    [Fact]
    public void ShortTapOfMainKeyCombo_CancelsAndReplaysOriginalShortcut()
    {
        var zoom = new HotkeyBinding("en", KeyCombo.Parse("Ctrl+-"), TriggerMode.Hold, HotkeyAction.Dictate, "translate");
        var m = new HotkeyMatcher([zoom], time: _time);
        m.OnKey(LControl, true);
        Assert.True(m.OnKey(OemMinus, true).Swallow);
        _time.Advance(TimeSpan.FromMilliseconds(100));
        var up = m.OnKey(OemMinus, false);
        Assert.True(up.Swallow);                         // Original-Up wurde verschluckt …
        Assert.Equal(HotkeyEventKind.Cancel, up.Event?.Kind);
        Assert.Equal(KeyCombo.Parse("Ctrl+-"), up.Replay); // … und die ganze Kombi wird nachgereicht.
    }

    [Fact]
    public void ShortTap_ReleasingCtrlFirst_AlsoReplays()
    {
        var b = new HotkeyBinding("d", KeyCombo.Parse("Ctrl+,"), TriggerMode.Hold, HotkeyAction.Dictate);
        var m = new HotkeyMatcher([b], time: _time);
        m.OnKey(LControl, true);
        m.OnKey(OemComma, true);
        var ctrlUp = m.OnKey(LControl, false);
        Assert.Equal(KeyCombo.Parse("Ctrl+,"), ctrlUp.Replay);
        Assert.True(m.OnKey(OemComma, false).Swallow);   // späteres Up der Komma-Taste bleibt verschluckt
    }

    [Fact]
    public void ModifierOnlyCombos_NeverReplay()
    {
        var m = Create();
        m.OnKey(LControl, true);
        m.OnKey(LWin, true);
        Assert.Null(m.OnKey(LWin, false).Replay);
    }

    [Fact]
    public void ExtraModifier_DoesNotMatchMainKeyCombo()
    {
        var m = Create();
        m.OnKey(LControl, true);
        m.OnKey(LMenu, true);
        m.OnKey(LShift, true);
        var r = m.OnKey(Space, true);
        Assert.False(r.Swallow);
        Assert.Null(r.Event);
    }

    [Fact]
    public void ToggleStartsAndStopsOnSecondPress()
    {
        var m = Create();
        Assert.Equal(HotkeyEventKind.Start, m.OnKey(0x78, true).Event?.Kind);
        Assert.Null(m.OnKey(0x78, false).Event);
        Assert.True(m.IsSessionActive);
        var stop = m.OnKey(0x78, true);
        Assert.Equal(HotkeyEventKind.Stop, stop.Event?.Kind);
        Assert.True(stop.Swallow);
    }

    [Fact]
    public void EscapeCancelsOnlyDuringSession()
    {
        var m = Create();
        Assert.False(m.OnKey(Escape, true).Swallow);
        m.OnKey(Escape, false);

        m.OnKey(0x78, true);
        m.OnKey(0x78, false);
        var esc = m.OnKey(Escape, true);
        Assert.True(esc.Swallow);
        Assert.Equal(HotkeyEventKind.Cancel, esc.Event?.Kind);
        Assert.True(m.OnKey(Escape, false).Swallow);
    }

    [Fact]
    public void LibraryToggleDoesNotStartSession()
    {
        var m = Create();
        m.OnKey(LControl, true);
        m.OnKey(LMenu, true);
        var r = m.OnKey('L', true);
        Assert.Equal(HotkeyEventKind.ToggleLibrary, r.Event?.Kind);
        Assert.False(m.IsSessionActive);
    }

    [Fact]
    public void ReleasingOneOfTwoCtrlKeys_KeepsRecording()
    {
        var m = Create();
        m.OnKey(LControl, true);
        m.OnKey(RControl, true);
        m.OnKey(LWin, true);
        Assert.True(m.IsSessionActive);
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(m.OnKey(LControl, false).Event);
        Assert.Equal(HotkeyEventKind.Stop, m.OnKey(RControl, false).Event?.Kind);
    }
}
