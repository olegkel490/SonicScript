using System.Text;
using SonicScript.Core;
using SonicScript.Core.Hotkeys;
using SonicScript.Core.Modes;
using SonicScript.Core.Notes;
using SonicScript.Core.Settings;
using SonicScript.Core.Storage;

namespace SonicScript.Core.Tests;

public class NoteStoreTests
{
    [Fact]
    public void AppendsIntoYearMonthDayFile()
    {
        using var dir = new TempDir();
        var store = new NoteStore(dir.Path);
        store.Append(new DateTime(2026, 6, 7, 9, 14, 0), "Idee für das Meeting:\nmit Beispiel starten");
        store.Append(new DateTime(2026, 6, 7, 10, 2, 0), "Zweite Notiz");

        var path = Path.Combine(dir.Path, "2026", "2026-06", "2026-06-07.md");
        Assert.Equal(path, store.PathFor(new DateOnly(2026, 6, 7)));
        Assert.Equal("# 2026-06-07\n\n- [ ] 09:14 Idee für das Meeting: mit Beispiel starten\n- [ ] 10:02 Zweite Notiz\n", File.ReadAllText(path));

        var notes = store.Read(new DateOnly(2026, 6, 7));
        Assert.Equal(2, notes.Count);
        Assert.Equal(new TimeOnly(10, 2), notes[1].Time);
        Assert.False(notes[0].Used);
    }

    [Fact]
    public void MarksUsedAndDeletes()
    {
        using var dir = new TempDir();
        var store = new NoteStore(dir.Path);
        var day = new DateOnly(2026, 11, 16);
        var t = day.ToDateTime(new TimeOnly(8, 0));
        store.Append(t, "eins");
        store.Append(t.AddMinutes(1), "zwei");
        store.Append(t.AddMinutes(2), "drei");

        var notes = store.Read(day);
        Assert.True(store.MarkUsed(day, notes[1]));
        Assert.False(store.MarkUsed(day, notes[1] with { Text = "geändert" })); // Inhalt passt nicht → nichts tun

        notes = store.Read(day);
        Assert.True(notes[1].Used);
        Assert.Equal(1, store.DeleteUsed(day));
        Assert.Equal(["eins", "drei"], store.Read(day).Select(n => n.Text));

        Assert.Equal(2, store.DeleteAll(day));
        Assert.False(File.Exists(store.PathFor(day)));
    }

    [Fact]
    public void ToleratesForeignLinesAndListsDays()
    {
        using var dir = new TempDir();
        var store = new NoteStore(dir.Path);
        var day = new DateOnly(2026, 3, 5);
        Directory.CreateDirectory(Path.GetDirectoryName(store.PathFor(day))!);
        File.WriteAllText(store.PathFor(day), "# Eigene Überschrift\nFreitext\n- [x] 07:00 alt");
        store.Append(day.ToDateTime(new TimeOnly(9, 30)), "neu");
        store.Append(new DateTime(2026, 3, 9, 9, 0, 0), "anderer Tag");

        var notes = store.Read(day);
        Assert.Equal(["alt", "neu"], notes.Select(n => n.Text));
        Assert.True(notes[0].Used);
        Assert.Equal([new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 9)], store.DaysWithNotes(2026, 3));
        Assert.Empty(store.DaysWithNotes(2026, 4));
    }
}

public class SecretAndHistoryTests
{
    [Fact]
    public void SecretsAreNotStoredInPlainText()
    {
        using var dir = new TempDir();
        var store = new SecretStore(dir.Path, new FakeProtector());
        store.Save("openrouter", "sk-or-test-123");
        Assert.Equal("sk-or-test-123", store.Load("openrouter"));
        var raw = File.ReadAllBytes(Path.Combine(dir.Path, "openrouter.secret"));
        Assert.DoesNotContain("sk-or", Encoding.UTF8.GetString(raw));

        store.Save("openrouter", "");
        Assert.False(store.Has("openrouter"));
        Assert.Throws<ArgumentException>(() => store.Save("../evil", "x"));
    }

    [Fact]
    public void HistoryIsEncryptedAndPruned()
    {
        using var dir = new TempDir();
        var time = new FakeTime(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        var path = Path.Combine(dir.Path, "history.bin");
        var h = new HistoryStore(path, new FakeProtector(), time);

        h.Add(new HistoryEntry(time.GetUtcNow().AddDays(-40), "verbatim", "alt", "alt", null), 3, 30);
        for (var i = 0; i < 5; i++)
            h.Add(new HistoryEntry(time.GetUtcNow(), "verbatim", "geheim" + i, "geheim" + i, "notepad"), 3, 30);

        var all = new HistoryStore(path, new FakeProtector(), time).GetAll();
        Assert.Equal(["geheim4", "geheim3", "geheim2"], all.Select(e => e.Final));
        Assert.DoesNotContain("geheim", Encoding.UTF8.GetString(File.ReadAllBytes(path)));

        h.Clear();
        Assert.Empty(h.GetAll());
    }

    [Fact]
    public void UnreadableHistoryStartsFresh()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "history.bin");
        File.WriteAllText(path, "kaputt");
        Assert.Empty(new HistoryStore(path, new FakeProtector()).GetAll());
    }

    [Fact]
    public void PendingRecordingsAreKeptAndExpire()
    {
        using var dir = new TempDir();
        var time = new FakeTime(DateTimeOffset.UtcNow);
        var pending = new PendingRecordings(Path.Combine(dir.Path, "pending"), time);
        var wav = Path.Combine(dir.Path, "a.wav");
        File.WriteAllBytes(wav, [1, 2, 3]);

        var rec = pending.Keep(wav, "improve", "timeout");
        Assert.False(File.Exists(wav));
        Assert.Single(pending.List());

        File.SetLastWriteTimeUtc(rec.AudioPath, DateTime.UtcNow.AddHours(-25));
        File.SetLastWriteTimeUtc(rec.AudioPath + ".json", DateTime.UtcNow.AddHours(-25));
        Assert.Equal(2, pending.Cleanup());
        Assert.Empty(pending.List());
    }
}

public class SettingsTests
{
    [Fact]
    public void RoundTripsAndRepairs()
    {
        using var dir = new TempDir();
        var store = new SettingsStore(Path.Combine(dir.Path, "settings.json"));
        var s = store.Load();
        Assert.Equal(6, s.Modes.Count);

        s.Modes.RemoveAll(m => m.Id == BuiltInModes.EmojiId);
        s.Modes.Add(new ModeDefinition { Name = "Übersetzen", Instruction = "Ins Englische", Hotkey = KeyCombo.Parse("Ctrl+Alt+T") });
        s.DefaultModeId = "gibt-es-nicht";
        s.History.MaxEntries = 99999;
        s.AppRules.Add(new AppRule { ProcessName = "outlook", ModeId = BuiltInModes.ImproveId, Insertion = InsertionMethod.Type });
        store.Save(s);

        var json = File.ReadAllText(store.Path);
        Assert.Contains("\"Ctrl+Alt+T\"", json);
        Assert.Contains("\"type\"", json);

        var loaded = store.Load();
        Assert.Equal(7, loaded.Modes.Count);                  // Emoji wieder ergänzt + eigener Modus
        Assert.Equal(BuiltInModes.VerbatimId, loaded.DefaultModeId);
        Assert.Equal(1000, loaded.History.MaxEntries);
        Assert.Equal(InsertionMethod.Type, loaded.AppRules.Last().Insertion);
        Assert.Contains(loaded.BuildHotkeyBindings(), b => b.Combo == KeyCombo.Parse("Ctrl+Alt+T"));
    }

    [Fact]
    public void BrokenFileFallsBackToDefaultsAndIsBackedUp()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, "{ kaputt");
        var s = new SettingsStore(path).Load();
        Assert.Equal(BuiltInModes.VerbatimId, s.DefaultModeId);
        Assert.True(File.Exists(path + ".broken"));
    }

    [Fact]
    public void DefaultHotkeysHaveNoConflicts()
    {
        var conflicts = SonicScript.Core.Hotkeys.HotkeyConflicts.Find(new AppSettings().BuildHotkeyBindings());
        Assert.Empty(conflicts);
    }
}

public class MigrationTests
{
    [Fact]
    public void ExistingSettingsGetTranslateModeWithoutHotkeyClash()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        // Alte Einstellungen ohne Übersetzen-Modus, eigener Modus belegt schon Strg+-.
        File.WriteAllText(path, """
        { "modes": [ { "id": "verbatim", "kind": "verbatim", "name": "A" },
                     { "id": "mine", "kind": "custom", "name": "Mein", "hotkey": "Ctrl+-" } ] }
        """);
        var s = new SettingsStore(path).Load();
        var translate = s.FindMode(BuiltInModes.TranslateId);
        Assert.NotNull(translate);
        Assert.True(translate!.Hotkey.IsEmpty);
        Assert.Empty(HotkeyConflicts.Find(s.BuildHotkeyBindings()));
    }

    [Fact]
    public void RemoveAllWorksOnObservableCollections()
    {
        var c = new System.Collections.ObjectModel.ObservableCollection<int>([1, 2, 3, 4]);
        Assert.Equal(2, c.RemoveAll(i => i % 2 == 0));
        Assert.Equal([1, 3], c);
    }
}

public class HotkeyMigrationTests
{
    [Fact]
    public void OldDefaultsMoveToNewKeys_CustomizedOnesStay()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, """
        {
          "dictateHotkey": "Ctrl+Win",
          "notes": { "hotkey": "Ctrl+Alt+Space" },
          "modes": [
            { "id": "verbatim", "kind": "verbatim", "name": "A" },
            { "id": "improve", "kind": "improve", "name": "B", "hotkey": "Ctrl+Shift+Win" },
            { "id": "calm", "kind": "calm", "name": "C", "hotkey": "F8" },
            { "id": "emoji", "kind": "emoji", "name": "D", "hotkey": "Alt+Win" },
            { "id": "translate", "kind": "translate", "name": "E", "hotkey": "Alt+Shift+Win" }
          ]
        }
        """);
        var store = new SettingsStore(path);
        var s = store.Load();
        Assert.Equal("Ctrl+Alt+,", s.DictateHotkey.ToString());              // Schema 5
        Assert.True(s.Notes.Hotkey.IsEmpty);                                 // ab Schema 4: Sprachbefehl "Notiz:"
        Assert.True(s.FindMode("improve")!.Hotkey.IsEmpty);                 // Schema 5: per Moduswechsel
        Assert.Equal("F8", s.FindMode("calm")!.Hotkey.ToString());          // selbst geändert → bleibt
        Assert.True(s.FindMode("emoji")!.Hotkey.IsEmpty);
        Assert.True(s.FindMode("translate")!.Hotkey.IsEmpty);
        Assert.Equal(AppSettings.CurrentSchema, s.SchemaVersion);

        // Nach der Migration wird nichts mehr umgestellt (z. B. wenn der Nutzer bewusst Ctrl+Win wählt).
        s.DictateHotkey = KeyCombo.Parse("Ctrl+Win");
        store.Save(s);
        Assert.Equal("Ctrl+Win", store.Load().DictateHotkey.ToString());
    }
}
