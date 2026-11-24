using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using SonicScript.Core.Hotkeys;
using SonicScript.Core.Modes;

namespace SonicScript.Core.Settings;

public sealed class AppSettings
{
    public const int CurrentSchema = 7;

    /// <summary>Fehlt in alten Dateien → 1 (Migration in <see cref="Normalize"/>).</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>"auto" (Systemsprache), "de" oder "en".</summary>
    public string UiLanguage { get; set; } = "auto";

    public bool Autostart { get; set; }
    public bool CheckForUpdates { get; set; } = true;
    public bool ShowRecordingIndicator { get; set; } = true;
    public bool FirstRunCompleted { get; set; }

    /// <summary>Sprachbefehle am Diktatanfang ("Prompt: …", "Englisch: …", "Notiz: …") wechseln den Modus.</summary>
    public bool VoiceKeywords { get; set; }

    /// <summary>Aktuell gewählter Modus für die Diktat-Taste (Moduswechsel mit Vor/Zurück). Auch "action:edit"/"action:note".</summary>
    public string CurrentModeId { get; set; } = BuiltInModes.VerbatimId;

    public KeyCombo NextModeHotkey { get; set; } = DefaultNextModeHotkey;
    public KeyCombo PrevModeHotkey { get; set; } = DefaultPrevModeHotkey;
    public static readonly KeyCombo DefaultNextModeHotkey = new(KeyModifiers.Ctrl | KeyModifiers.Alt, VirtualKeys.OemPeriod);
    public static readonly KeyCombo DefaultPrevModeHotkey = new(KeyModifiers.Ctrl | KeyModifiers.Alt, VirtualKeys.OemMinus);

    /// <summary>Letztes Diktat zurücknehmen: 1. Druck wortgetreue Fassung statt KI-Text, 2. Druck ganz entfernen.</summary>
    public KeyCombo UndoHotkey { get; set; } = DefaultUndoHotkey;
    public static readonly KeyCombo DefaultUndoHotkey = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 'Z');

    /// <summary>Weitersprechen: an das letzte Diktat anhängen (KI-Modi glätten dann alles zusammen neu).</summary>
    public KeyCombo ContinueHotkey { get; set; } = DefaultContinueHotkey;
    public TriggerMode ContinueTrigger { get; set; } = TriggerMode.Hold;
    /// <summary>Strg+Alt+# – liegt auf deutschen Tastaturen direkt neben den anderen SonicScript-Tasten.</summary>
    public static readonly KeyCombo DefaultContinueHotkey = new(KeyModifiers.Ctrl | KeyModifiers.Alt, VirtualKeys.Oem2);

    /// <summary>Während der Aufnahme den bisher erkannten Text anzeigen (nur lokale Erkennung).</summary>
    public bool LivePreview { get; set; } = true;

    /// <summary>Hinweis "aus OneDrive umziehen" nicht mehr zeigen.</summary>
    public bool MoveOfferDismissed { get; set; }

    /// <summary>Monatliches Kostenlimit für OpenRouter in US-Dollar; 0 = keine Warnung.</summary>
    public double CostLimitMonthlyUsd { get; set; }
    /// <summary>Monat ("yyyy-MM"), für den schon gewarnt wurde.</summary>
    public string? CostWarnedMonth { get; set; }

    /// <summary>"Bearbeiten" und "Notiz" als Einträge im Moduswechsel.</summary>
    public bool EditInCycle { get; set; } = true;
    public bool NoteInCycle { get; set; } = true;

    /// <summary>Kleines, immer sichtbares Abzeichen mit dem aktuellen Modus.</summary>
    public bool ShowModeBadge { get; set; } = true;
    public int? ModeBadgeX { get; set; }
    public int? ModeBadgeY { get; set; }

    /// <summary>App-Regeln dürfen den Modus der Diktat-Taste überschreiben (sonst gilt immer der gewählte Modus).</summary>
    public bool AppRulesSelectMode { get; set; }

    /// <summary>Kurzer Ton bei Start und Ende einer Aufnahme.</summary>
    public bool PlaySounds { get; set; } = true;

    /// <summary>Stille am Anfang/Ende abschneiden und lange Pausen kürzen, bevor erkannt wird.</summary>
    public bool TrimSilence { get; set; } = true;

    /// <summary>Andere Audioquellen (Musik, Videos) während der Aufnahme leiser stellen.</summary>
    public bool DuckOtherAudio { get; set; } = true;

    /// <summary>Lautstärke der anderen Audioquellen während der Aufnahme in Prozent (0 = stumm).</summary>
    public int DuckLevelPercent { get; set; } = 15;

    /// <summary>Markierten Text per Sprache bearbeiten ("mach das kürzer").</summary>
    public KeyCombo EditHotkey { get; set; } = KeyCombo.None;
    public TriggerMode EditTrigger { get; set; } = TriggerMode.Hold;

    /// <summary>Standard bis Schema 4 (seit Schema 5: "Bearbeiten" im Moduswechsel).</summary>
    public static readonly KeyCombo DefaultEditHotkey = new(KeyModifiers.Ctrl | KeyModifiers.Alt, VirtualKeys.OemPeriod);

    public TranscriptionSettings Transcription { get; set; } = new();
    public LanguageModelSettings LanguageModel { get; set; } = new();
    public ObservableCollection<ModeDefinition> Modes { get; set; } = new(BuiltInModes.CreateDefaults());

    /// <summary>Diktat im aktuell gewählten Modus (<see cref="CurrentModeId"/>).</summary>
    public KeyCombo DictateHotkey { get; set; } = DefaultDictateHotkey;

    public static readonly KeyCombo DefaultDictateHotkey = new(KeyModifiers.Ctrl | KeyModifiers.Alt, VirtualKeys.OemComma);
    internal static readonly KeyCombo Schema4DictateHotkey = new(KeyModifiers.Ctrl, VirtualKeys.OemComma);
    public TriggerMode DictateTrigger { get; set; } = TriggerMode.Hold;
    public string DefaultModeId { get; set; } = BuiltInModes.VerbatimId;
    public ObservableCollection<AppRule> AppRules { get; set; } = new(DefaultAppRules());

    /// <summary>Prompt-Modus automatisch in KI-Chats und -Editoren.</summary>
    public static IEnumerable<AppRule> DefaultAppRules() =>
    [
        new() { TitleContains = "ChatGPT", ModeId = BuiltInModes.PromptId },
        new() { TitleContains = "Claude", ModeId = BuiltInModes.PromptId },
        new() { TitleContains = "Gemini", ModeId = BuiltInModes.PromptId },
        new() { TitleContains = "Perplexity", ModeId = BuiltInModes.PromptId },
        new() { TitleContains = "Copilot", ModeId = BuiltInModes.PromptId },
        new() { ProcessName = "Cursor", ModeId = BuiltInModes.PromptId },
        new() { ProcessName = "Windsurf", ModeId = BuiltInModes.PromptId },
    ];

    public InsertionSettings Insertion { get; set; } = new();
    public NotesSettings Notes { get; set; } = new();
    public DictionarySettings Dictionary { get; set; } = new();
    public HistorySettings History { get; set; } = new();

    /// <summary>
    /// Schema 1 → 2: Standardtasten von Ctrl/Alt/Shift+Win auf Strg + , . - umstellen. Nur Tasten, die der Nutzer
    /// nicht selbst geändert hat, und nur wenn die neue Taste nicht schon anderweitig belegt ist.
    /// </summary>
    private void MigrateHotkeys()
    {
        if (SchemaVersion >= CurrentSchema) return;
        if (SchemaVersion < 2) MigrateToSchema2();
        if (SchemaVersion < 3)
        {
            // Neue Taste "Auswahl bearbeiten": nur übernehmen, wenn frei.
            if (EditHotkey == DefaultEditHotkey &&
                (DictateHotkey == EditHotkey || Notes.Hotkey == EditHotkey || Notes.LibraryHotkey == EditHotkey || Modes.Any(m => m.Hotkey == EditHotkey)))
                EditHotkey = KeyCombo.None;
        }
        if (SchemaVersion < 4) MigrateToSchema4();
        if (SchemaVersion < 5) MigrateToSchema5();
        if (SchemaVersion < 6) MigrateToSchema6();
        if (SchemaVersion < 7) MigrateToSchema7();
        SchemaVersion = CurrentSchema;
    }

    /// <summary>
    /// Schema 5: Strg+Alt+, diktiert im gewählten Modus, Strg+Alt+. / Strg+Alt+- wechseln den Modus.
    /// Unveränderte alte Tasten (Strg+, / Strg+. / Bearbeiten Strg+Alt+.) werden umgestellt, Sprachbefehle aus.
    /// </summary>
    /// <summary>Schema 6: neue Rückgängig-Taste nur, wenn sie noch frei ist.</summary>
    private void MigrateToSchema6()
    {
        var others = BuildHotkeyBindings().Where(b => b.Id != "undo").Select(b => b.Combo);
        if (others.Contains(UndoHotkey)) UndoHotkey = KeyCombo.None;
    }

    /// <summary>Schema 7: neue Fortsetzen-Taste nur, wenn sie noch frei ist.</summary>
    private void MigrateToSchema7()
    {
        var others = BuildHotkeyBindings().Where(b => b.Id != "continue").Select(b => b.Combo);
        if (others.Contains(ContinueHotkey)) ContinueHotkey = KeyCombo.None;
    }

    private void MigrateToSchema5()
    {
        CurrentModeId = DefaultModeId;
        VoiceKeywords = false;
        if (EditHotkey == DefaultEditHotkey) EditHotkey = KeyCombo.None;
        var improve = FindMode(BuiltInModes.ImproveId);
        if (improve is not null && improve.Hotkey == new KeyCombo(KeyModifiers.Ctrl, VirtualKeys.OemPeriod)) improve.Hotkey = KeyCombo.None;
        if (DictateHotkey == Schema4DictateHotkey) DictateHotkey = DefaultDictateHotkey;

        // Neue Wechsel-Tasten nur, wenn frei.
        bool TakenByOthers(KeyCombo c) => c == DictateHotkey || c == EditHotkey || c == Notes.Hotkey || c == Notes.LibraryHotkey || Modes.Any(m => m.Hotkey == c);
        if (TakenByOthers(NextModeHotkey)) NextModeHotkey = KeyCombo.None;
        if (TakenByOthers(PrevModeHotkey)) PrevModeHotkey = KeyCombo.None;
    }

    /// <summary>
    /// Schema 4: weniger Tasten. Unveränderte Standardtasten für Ruhig, Emoji, EN und Notiz entfallen (Sprachbefehle
    /// ersetzen sie), eingebaute Modi bekommen ihre Stichwörter, Prompt-Regeln für KI-Apps kommen dazu.
    /// </summary>
    private void MigrateToSchema4()
    {
        foreach (var m in Modes)
        {
            if (BuiltInModes.Schema3Hotkeys.TryGetValue(m.Id, out var old) && m.Hotkey == old) m.Hotkey = KeyCombo.None;
            if (string.IsNullOrWhiteSpace(m.Keywords) && BuiltInModes.DefaultKeywords.TryGetValue(m.Id, out var kw)) m.Keywords = kw;
        }
        if (Notes.Hotkey == NotesSettings.Schema3Hotkey) Notes.Hotkey = KeyCombo.None;
        if (string.IsNullOrWhiteSpace(Notes.Keywords)) Notes.Keywords = NotesSettings.DefaultKeywords;

        foreach (var rule in DefaultAppRules())
        {
            var exists = AppRules.Any(r =>
                string.Equals(r.ProcessName.Trim(), rule.ProcessName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.TitleContains.Trim(), rule.TitleContains, StringComparison.OrdinalIgnoreCase));
            if (!exists) AppRules.Add(rule);
        }
    }

    private void MigrateToSchema2()
    {
        var defaults = BuiltInModes.CreateDefaults();
        bool Taken(KeyCombo c) => c == DictateHotkey || c == Notes.Hotkey || c == Notes.LibraryHotkey || Modes.Any(m => m.Hotkey == c);

        var oldDictate = new KeyCombo(KeyModifiers.Ctrl | KeyModifiers.Win, 0);
        if (DictateHotkey == oldDictate && !Taken(Schema4DictateHotkey)) DictateHotkey = Schema4DictateHotkey;

        var oldNote = new KeyCombo(KeyModifiers.Ctrl | KeyModifiers.Alt, VirtualKeys.Space);
        if (Notes.Hotkey == oldNote && !Taken(NotesSettings.Schema3Hotkey)) Notes.Hotkey = NotesSettings.Schema3Hotkey;

        foreach (var m in Modes)
        {
            if (!BuiltInModes.LegacyHotkeys.TryGetValue(m.Id, out var legacy) || m.Hotkey != legacy) continue;
            var target = BuiltInModes.Schema3Hotkeys.TryGetValue(m.Id, out var s3) ? s3
                : m.Id == BuiltInModes.ImproveId ? new KeyCombo(KeyModifiers.Ctrl, VirtualKeys.OemPeriod)
                : defaults.First(d => d.Id == m.Id).Hotkey;
            if (!Taken(target)) m.Hotkey = target;
        }
    }

    public ModeDefinition? FindMode(string? id) => id is null ? null : Modes.FirstOrDefault(m => m.Id == id);

    public ModeDefinition DefaultMode => FindMode(DefaultModeId) ?? Modes.First(m => m.Kind == ModeKind.Verbatim);

    public IEnumerable<HotkeyBinding> BuildHotkeyBindings()
    {
        yield return new HotkeyBinding("dictate", DictateHotkey, DictateTrigger, HotkeyAction.Dictate);
        foreach (var m in Modes.Where(m => !m.Hotkey.IsEmpty))
            yield return new HotkeyBinding("mode:" + m.Id, m.Hotkey, m.Trigger, HotkeyAction.Dictate, m.Id);
        yield return new HotkeyBinding("note", Notes.Hotkey, Notes.Trigger, HotkeyAction.Note, Notes.ModeId);
        yield return new HotkeyBinding("library", Notes.LibraryHotkey, TriggerMode.Toggle, HotkeyAction.ToggleLibrary);
        yield return new HotkeyBinding("edit", EditHotkey, EditTrigger, HotkeyAction.EditSelection);
        yield return new HotkeyBinding("next", NextModeHotkey, TriggerMode.Toggle, HotkeyAction.NextMode);
        yield return new HotkeyBinding("prev", PrevModeHotkey, TriggerMode.Toggle, HotkeyAction.PreviousMode);
        yield return new HotkeyBinding("undo", UndoHotkey, TriggerMode.Toggle, HotkeyAction.Undo);
        yield return new HotkeyBinding("continue", ContinueHotkey, ContinueTrigger, HotkeyAction.Continue);
    }

    public const string EditEntryId = "action:edit";
    public const string NoteEntryId = "action:note";

    /// <summary>Reihenfolge für den Moduswechsel: Modi mit "Im Wechsel", dann Bearbeiten und Notiz.</summary>
    public IReadOnlyList<CycleEntry> BuildCycle()
    {
        var list = Modes.Where(m => m.InCycle).Select(m => new CycleEntry(m.Id, m.Name, m.Symbol, CycleKind.Mode, m)).ToList();
        if (EditInCycle) list.Add(new CycleEntry(EditEntryId, Localization.Strings.Get("cycle.edit"), "✂", CycleKind.Edit, null));
        if (NoteInCycle) list.Add(new CycleEntry(NoteEntryId, Localization.Strings.Get("cycle.note"), "▤", CycleKind.Note, null));
        if (list.Count == 0) list.Add(new CycleEntry(DefaultMode.Id, DefaultMode.Name, DefaultMode.Symbol, CycleKind.Mode, DefaultMode));
        return list;
    }

    public CycleEntry CurrentEntry
    {
        get
        {
            var cycle = BuildCycle();
            return cycle.FirstOrDefault(e => e.Id == CurrentModeId) ?? cycle[0];
        }
    }

    /// <summary>Nächsten (+1) bzw. vorherigen (-1) Eintrag wählen, mit Umlauf.</summary>
    public CycleEntry StepMode(int direction)
    {
        var cycle = BuildCycle();
        var idx = cycle.ToList().FindIndex(e => e.Id == CurrentModeId);
        if (idx < 0) idx = 0;
        else idx = ((idx + Math.Sign(direction)) % cycle.Count + cycle.Count) % cycle.Count;
        CurrentModeId = cycle[idx].Id;
        return cycle[idx];
    }

    /// <summary>Nach dem Laden reparieren: fehlende Teile ergänzen, Werte begrenzen.</summary>
    public void Normalize()
    {
        Transcription ??= new();
        LanguageModel ??= new();
        Modes ??= new(BuiltInModes.CreateDefaults());
        AppRules ??= [];
        Insertion ??= new();
        Notes ??= new();
        Dictionary ??= new();
        History ??= new();
        Dictionary.Terms ??= [];
        Dictionary.Replacements ??= [];

        MigrateHotkeys();
        BuiltInModes.EnsurePresent(Modes);
        if (FindMode(DefaultModeId) is null) DefaultModeId = BuiltInModes.VerbatimId;
        if (!BuildCycle().Any(e => e.Id == CurrentModeId)) CurrentModeId = BuildCycle()[0].Id;
        if (FindMode(Notes.ModeId) is null) Notes.ModeId = BuiltInModes.VerbatimId;
        foreach (var m in Modes) m.Temperature = Math.Clamp(m.Temperature, 0, 1.5);

        History.MaxEntries = Math.Clamp(History.MaxEntries, 0, 1000);
        History.MaxAgeDays = Math.Clamp(History.MaxAgeDays, 1, 365);
        Insertion.ClearClipboardAfterSeconds = Math.Clamp(Insertion.ClearClipboardAfterSeconds, 0, 3600);
        Insertion.RestoreDelayMs = Math.Clamp(Insertion.RestoreDelayMs, 100, 5000);
        DuckLevelPercent = Math.Clamp(DuckLevelPercent, 0, 100);
        Dictionary.IgnoredSuggestions ??= [];
        CostLimitMonthlyUsd = Math.Clamp(CostLimitMonthlyUsd, 0, 100000);
    }
}

public enum CycleKind
{
    Mode,
    Edit,
    Note,
}

/// <summary>Ein Eintrag im Moduswechsel (Modus, "Bearbeiten" oder "Notiz").</summary>
public sealed record CycleEntry(string Id, string Name, string Symbol, CycleKind Kind, ModeDefinition? Mode);

public enum TranscriptionEngine
{
    Local,
    Cloud,
}

public sealed class TranscriptionSettings
{
    public TranscriptionEngine Engine { get; set; } = TranscriptionEngine.Local;

    /// <summary>ID aus dem <see cref="Transcription.WhisperModelCatalog"/>.</summary>
    public string LocalModelId { get; set; } = "large-v3-turbo-q5_0";

    public bool UseGpu { get; set; } = true;

    /// <summary>"auto" oder ISO-Code ("de", "en", …).</summary>
    public string Language { get; set; } = "auto";

    public int MicrophoneDevice { get; set; } = -1; // -1 = Standardgerät

    /// <summary>Name des gewählten Mikrofons – die Nummern verschieben sich beim An-/Abstecken von Geräten.</summary>
    public string MicrophoneName { get; set; } = "";

    /// <summary>Fällt die Cloud-Erkennung aus, lokal erkennen (wenn ein lokales Modell installiert ist).</summary>
    public bool FallbackToLocal { get; set; } = true;

    public EndpointSettings Cloud { get; set; } = new()
    {
        Preset = ProviderPreset.OpenRouter,
        BaseUrl = ProviderPresets.BaseUrl(ProviderPreset.OpenRouter),
        Model = "openai/whisper-large-v3",
    };
}

public sealed class LanguageModelSettings
{
    public EndpointSettings Endpoint { get; set; } = new()
    {
        Preset = ProviderPreset.Ollama,
        BaseUrl = ProviderPresets.BaseUrl(ProviderPreset.Ollama),
        Model = "qwen3:4b",
    };

    /// <summary>Ersatzmodell beim selben Anbieter, falls das Hauptmodell ausfällt (leer = keins).</summary>
    public string FallbackModel { get; set; } = "";
}

public enum ProviderPreset
{
    Ollama,
    LmStudio,
    OpenRouter,
    OpenAI,
    Custom,
}

public sealed class EndpointSettings
{
    public ProviderPreset Preset { get; set; } = ProviderPreset.Custom;
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";

    /// <summary>Name des Geheimnisses im <see cref="Storage.SecretStore"/> (nie der Schlüssel selbst).</summary>
    public string SecretName { get; set; } = "";

    [JsonIgnore]
    public bool IsLocal => Net.EndpointPolicy.IsLocalOrPrivate(BaseUrl);
}

public static class ProviderPresets
{
    public static string BaseUrl(ProviderPreset p) => p switch
    {
        ProviderPreset.Ollama => "http://localhost:11434/v1",
        ProviderPreset.LmStudio => "http://localhost:1234/v1",
        ProviderPreset.OpenRouter => "https://openrouter.ai/api/v1",
        ProviderPreset.OpenAI => "https://api.openai.com/v1",
        _ => "",
    };

    public static bool NeedsApiKey(ProviderPreset p) => p is ProviderPreset.OpenRouter or ProviderPreset.OpenAI;
}

public enum InsertionMethod
{
    /// <summary>Zwischenablage + Strg+V.</summary>
    Paste,
    /// <summary>Zwischenablage + Strg+Shift+V (manche Terminals).</summary>
    PasteShift,
    /// <summary>Unicode-Tastendrücke simulieren.</summary>
    Type,
    /// <summary>Nur in die Zwischenablage.</summary>
    ClipboardOnly,
}

public sealed class InsertionSettings
{
    public InsertionMethod Method { get; set; } = InsertionMethod.Paste;

    /// <summary>Wenn Einfügen per Zwischenablage nicht ankommt (z. B. blockiert), Text tippen.</summary>
    public bool TypeAsFallback { get; set; } = true;

    public bool RestoreClipboard { get; set; } = true;

    /// <summary>Leerzeichen und Groß-/Kleinschreibung an den Text vor dem Cursor anpassen.</summary>
    public bool FitToContext { get; set; } = true;
    public int RestoreDelayMs { get; set; } = 800;

    /// <summary>0 = aus. Nur wirksam, wenn <see cref="RestoreClipboard"/> aus ist.</summary>
    public int ClearClipboardAfterSeconds { get; set; }
}

public sealed class AppRule
{
    /// <summary>Prozessname ohne ".exe", z. B. "outlook". Leer = jede App.</summary>
    public string ProcessName { get; set; } = "";

    /// <summary>Teil des Fenstertitels, z. B. "Gmail" für Webseiten im Browser. Leer = egal.</summary>
    public string TitleContains { get; set; } = "";

    public string ModeId { get; set; } = BuiltInModes.VerbatimId;
    public InsertionMethod? Insertion { get; set; }
}

public sealed class NotesSettings
{
    public string Folder { get; set; } = "";
    /// <summary>Standardtaste in Schema 2/3; seit Schema 4 ohne Taste (Sprachbefehl "Notiz: …").</summary>
    public static readonly KeyCombo Schema3Hotkey = new(KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeys.OemMinus);

    public const string DefaultKeywords = "Notiz, Note, Merken";

    public KeyCombo Hotkey { get; set; } = KeyCombo.None;

    /// <summary>Sprachbefehle, die ein Diktat als Notiz ablegen statt einzufügen.</summary>
    public string Keywords { get; set; } = DefaultKeywords;
    public TriggerMode Trigger { get; set; } = TriggerMode.Hold;
    public string ModeId { get; set; } = BuiltInModes.VerbatimId;
    public KeyCombo LibraryHotkey { get; set; } = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 'L');
    public bool CopyToClipboard { get; set; } = true;

    /// <summary>Zeitangaben in Notizen ("morgen 9 Uhr …") werden zu Erinnerungen.</summary>
    public bool Reminders { get; set; } = true;
    public DockSide LibraryDock { get; set; } = DockSide.Right;
}

public enum DockSide
{
    Right,
    Left,
}

public sealed class DictionarySettings
{
    /// <summary>Namen/Fachbegriffe in gewünschter Schreibweise; gehen als Hinweis an Whisper und das Sprachmodell.</summary>
    public List<string> Terms { get; set; } = [];

    public ObservableCollection<Replacement> Replacements { get; set; } = [];

    /// <summary>Korrekturen nach dem Einfügen erkennen und fürs Wörterbuch vorschlagen.</summary>
    public bool LearnCorrections { get; set; } = true;

    /// <summary>Abgelehnte Vorschläge ("von→zu"), die nicht erneut angeboten werden.</summary>
    public List<string> IgnoredSuggestions { get; set; } = [];

    public static string SuggestionKey(string from, string to) => from.ToLowerInvariant() + "→" + to;

    /// <summary>Vorschlag übernehmen: Ersetzung (falls anders geschrieben) und Begriff (als Hinweis für die Erkennung).</summary>
    public void Accept(string from, string to)
    {
        if (!string.Equals(from, to, StringComparison.OrdinalIgnoreCase) &&
            !Replacements.Any(r => string.Equals(r.From, from, StringComparison.OrdinalIgnoreCase)))
            Replacements.Add(new Replacement { From = from, To = to });
        if (!Terms.Contains(to)) Terms.Add(to);
    }
}

public sealed class Replacement
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}

public sealed class HistorySettings
{
    public bool Enabled { get; set; } = true;
    public int MaxEntries { get; set; } = 100;
    public int MaxAgeDays { get; set; } = 30;
}
