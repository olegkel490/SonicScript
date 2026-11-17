using SonicScript.Core.Hotkeys;
using SonicScript.Core.Settings;

namespace SonicScript.Core.Tests;

public class ModeCycleTests
{
    [Fact]
    public void CycleContainsModesThenEditAndNote()
    {
        var s = new AppSettings();
        var ids = s.BuildCycle().Select(e => e.Id).ToList();
        Assert.Equal(["verbatim", "improve", "calm", "emoji", "translate", "prompt", AppSettings.EditEntryId, AppSettings.NoteEntryId], ids);
    }

    [Fact]
    public void StepsForwardAndBackWithWrapAround()
    {
        var s = new AppSettings();
        Assert.Equal("improve", s.StepMode(+1).Id);
        Assert.Equal("verbatim", s.StepMode(-1).Id);
        Assert.Equal(AppSettings.NoteEntryId, s.StepMode(-1).Id);   // Umlauf rückwärts
        Assert.Equal("verbatim", s.StepMode(+1).Id);                 // Umlauf vorwärts
        Assert.Equal("verbatim", s.CurrentModeId);
    }

    [Fact]
    public void ExcludedModesAreSkipped_AndInvalidCurrentIsRepaired()
    {
        var s = new AppSettings { EditInCycle = false, NoteInCycle = false };
        foreach (var m in s.Modes.Where(m => m.Id is "calm" or "emoji")) m.InCycle = false;
        Assert.Equal(["verbatim", "improve", "translate", "prompt"], s.BuildCycle().Select(e => e.Id));

        s.CurrentModeId = "calm";       // nicht mehr im Wechsel
        s.Normalize();
        Assert.Equal("verbatim", s.CurrentModeId);
    }

    [Fact]
    public void CurrentEntryKnowsKind()
    {
        var s = new AppSettings { CurrentModeId = AppSettings.EditEntryId };
        Assert.Equal(CycleKind.Edit, s.CurrentEntry.Kind);
        s.CurrentModeId = "prompt";
        Assert.Equal(CycleKind.Mode, s.CurrentEntry.Kind);
        Assert.Equal("prompt", s.CurrentEntry.Mode!.Id);
    }

    [Fact]
    public void NextAndPreviousKeysAreCommandsWithoutSession()
    {
        var s = new AppSettings();
        s.Normalize();
        var m = new HotkeyMatcher(s.BuildHotkeyBindings());
        m.OnKey(VirtualKeys.LControl, true);
        m.OnKey(VirtualKeys.LMenu, true);
        var next = m.OnKey(VirtualKeys.OemPeriod, true);
        Assert.Equal(HotkeyEventKind.NextMode, next.Event?.Kind);
        Assert.True(next.Swallow);
        Assert.False(m.IsSessionActive);
        Assert.Null(m.OnKey(VirtualKeys.OemPeriod, false).Event);
        var prev = m.OnKey(VirtualKeys.OemMinus, true);
        Assert.Equal(HotkeyEventKind.PreviousMode, prev.Event?.Kind);
    }

    [Fact]
    public void Schema4UsersMoveToNewScheme()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, """
        { "schemaVersion": 4, "defaultModeId": "improve", "voiceKeywords": true,
          "dictateHotkey": "Ctrl+,", "editHotkey": "Ctrl+Alt+.",
          "modes": [ { "id": "improve", "kind": "improve", "name": "B", "hotkey": "Ctrl+." } ] }
        """);
        var s = new SettingsStore(path).Load();
        Assert.Equal("Ctrl+Alt+,", s.DictateHotkey.ToString());
        Assert.Equal("Ctrl+Alt+.", s.NextModeHotkey.ToString());
        Assert.Equal("Ctrl+Alt+-", s.PrevModeHotkey.ToString());
        Assert.True(s.EditHotkey.IsEmpty);
        Assert.True(s.FindMode("improve")!.Hotkey.IsEmpty);
        Assert.False(s.VoiceKeywords);
        Assert.Equal("improve", s.CurrentModeId);   // bisheriger Standardmodus bleibt ausgewählt
        Assert.Empty(HotkeyConflicts.Find(s.BuildHotkeyBindings()));
    }
}
