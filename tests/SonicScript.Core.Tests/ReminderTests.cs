using SonicScript.Core.Hotkeys;
using SonicScript.Core.Modes;
using SonicScript.Core.Reminders;
using SonicScript.Core.Settings;

namespace SonicScript.Core.Tests;

public class ReminderParserTests
{
    // Dienstag, 24.11.2026, 12:00
    private static readonly DateTime Now = new(2026, 11, 24, 12, 0, 0);

    [Theory]
    [InlineData("morgen 9 Uhr Angebot an Müller", "2026-11-25 09:00")]
    [InlineData("Morgen um 14:30 Anruf bei Anna", "2026-11-25 14:30")]
    [InlineData("übermorgen Rechnung prüfen", "2026-11-26 09:00")]
    [InlineData("am Freitag um 10 Uhr Team-Meeting vorbereiten", "2026-11-27 10:00")]
    [InlineData("Montag Angebot nachfassen", "2026-11-30 09:00")]
    [InlineData("am Dienstag Bericht", "2026-12-01 09:00")]     // heute ist Dienstag → nächste Woche
    [InlineData("um 15 Uhr Paket abholen", "2026-11-24 15:00")]
    [InlineData("um 11 Uhr Paket abholen", "2026-11-25 11:00")] // schon vorbei → morgen
    [InlineData("heute 16:45 Uhr Zahnarzt", "2026-11-24 16:45")]
    [InlineData("in 2 Stunden Ofen ausmachen", "2026-11-24 14:00")]
    [InlineData("in zehn Minuten zurückrufen", "2026-11-24 12:10")]
    [InlineData("in einer halben Stunde Pause", "2026-11-24 12:30")]
    [InlineData("in 3 Tagen Rückmeldung", "2026-11-27 09:00")]
    [InlineData("24.12. 10 Uhr Geschenke", "2026-12-24 10:00")]
    [InlineData("am 3.1. Steuer", "2027-01-03 09:00")]
    [InlineData("tomorrow at 9 am call John", "2026-11-25 09:00")]
    [InlineData("call Lisa at 3 pm", "2026-11-24 15:00")]
    [InlineData("morgen um 9 Uhr 30 Termin", "2026-11-25 09:30")]
    public void FindsDueTimes(string text, string expected) =>
        Assert.Equal(DateTime.Parse(expected), ReminderParser.Parse(text, Now)!.Due);

    [Theory]
    [InlineData("Idee für den Newsletter: mehr Bilder")]
    [InlineData("Das Meeting war 14:00 ganz gut")]
    [InlineData("3 Äpfel und 5 Birnen kaufen")]
    [InlineData("Heute Morgen war es kalt")]
    [InlineData("")]
    public void IgnoresTextWithoutReminder(string text) => Assert.Null(ReminderParser.Parse(text, Now));
}

public class ReminderStoreTests
{
    [Fact]
    public void DueSnoozeCompleteAndIgnore()
    {
        using var dir = new TempDir();
        var store = new ReminderStore(Path.Combine(dir.Path, "r.json"));
        var now = new DateTime(2026, 11, 24, 12, 0, 0);
        var a = store.Add(now.AddMinutes(-1), "A");
        var b = store.Add(now.AddHours(1), "B");
        Assert.Equal(["A"], store.DueAt(now).Select(r => r.Text));

        store.Snooze(a.Id, now.AddMinutes(10));
        Assert.Empty(store.DueAt(now));
        Assert.Single(store.DueAt(now.AddMinutes(11)));

        store.Complete(a.Id);
        Assert.Equal(["B"], store.Pending().Select(r => r.Text));

        for (var i = 0; i < ReminderStore.MaxShown; i++) store.Ignored(b.Id, now);
        Assert.Empty(store.Pending());
    }
}

public class ContinueAndTemplateTests
{
    [Fact]
    public void ContinueHotkeyDefaultsAndIsRecordingBinding()
    {
        var s = new AppSettings();
        var b = Assert.Single(s.BuildHotkeyBindings(), b => b.Id == "continue");
        Assert.Equal(new KeyCombo(KeyModifiers.Ctrl | KeyModifiers.Alt, VirtualKeys.Oem2), b.Combo);
        var m = new HotkeyMatcher(s.BuildHotkeyBindings());
        m.OnKey(0x11, true);
        m.OnKey(0x12, true);
        var r = m.OnKey(VirtualKeys.Oem2, true);
        Assert.Equal(HotkeyEventKind.Start, r.Event!.Value.Kind);
        Assert.Equal(HotkeyAction.Continue, r.Event.Value.Binding!.Action);
    }

    [Fact]
    public void MigrationDropsContinueKeyIfTaken()
    {
        var s = new AppSettings { SchemaVersion = 6, EditHotkey = AppSettings.DefaultContinueHotkey };
        s.Normalize();
        Assert.Equal(KeyCombo.None, s.ContinueHotkey);
    }

    [Fact]
    public void TemplatesCreateCustomModesInBothLanguages()
    {
        Assert.True(ModeTemplates.All.Count >= 8);
        Assert.Equal(ModeTemplates.All.Count, ModeTemplates.All.Select(t => t.Key).Distinct().Count());
        foreach (var t in ModeTemplates.All)
        {
            var de = t.Create(true);
            var en = t.Create(false);
            Assert.Equal(ModeKind.Custom, de.Kind);
            Assert.Contains("Diktat", de.Instruction + "Diktat");
            Assert.NotEqual(de.Instruction, en.Instruction);
            Assert.NotEqual(de.Id, t.Create(true).Id); // jede Anlage eigene Id
            Assert.Contains(de.Instruction, PromptBuilder.BuildSystemPrompt(de, "de", []));
        }
    }
}
