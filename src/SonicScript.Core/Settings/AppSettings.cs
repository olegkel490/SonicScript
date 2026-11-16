using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using SonicScript.Core.Hotkeys;
using SonicScript.Core.Modes;

namespace SonicScript.Core.Settings;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>"auto" (Systemsprache), "de" oder "en".</summary>
    public string UiLanguage { get; set; } = "auto";

    public bool Autostart { get; set; }
    public bool CheckForUpdates { get; set; } = true;
    public bool ShowRecordingIndicator { get; set; } = true;
    public bool FirstRunCompleted { get; set; }

    public TranscriptionSettings Transcription { get; set; } = new();
    public LanguageModelSettings LanguageModel { get; set; } = new();
    public ObservableCollection<ModeDefinition> Modes { get; set; } = new(BuiltInModes.CreateDefaults());

    /// <summary>Standard-Diktat: Modus kommt aus <see cref="AppRules"/>, sonst <see cref="DefaultModeId"/>.</summary>
    public KeyCombo DictateHotkey { get; set; } = new(KeyModifiers.Ctrl | KeyModifiers.Win, 0);
    public TriggerMode DictateTrigger { get; set; } = TriggerMode.Hold;
    public string DefaultModeId { get; set; } = BuiltInModes.VerbatimId;
    public ObservableCollection<AppRule> AppRules { get; set; } = [];

    public InsertionSettings Insertion { get; set; } = new();
    public NotesSettings Notes { get; set; } = new();
    public DictionarySettings Dictionary { get; set; } = new();
    public HistorySettings History { get; set; } = new();

    public ModeDefinition? FindMode(string? id) => id is null ? null : Modes.FirstOrDefault(m => m.Id == id);

    public ModeDefinition DefaultMode => FindMode(DefaultModeId) ?? Modes.First(m => m.Kind == ModeKind.Verbatim);

    public IEnumerable<HotkeyBinding> BuildHotkeyBindings()
    {
        yield return new HotkeyBinding("dictate", DictateHotkey, DictateTrigger, HotkeyAction.Dictate);
        foreach (var m in Modes.Where(m => !m.Hotkey.IsEmpty))
            yield return new HotkeyBinding("mode:" + m.Id, m.Hotkey, m.Trigger, HotkeyAction.Dictate, m.Id);
        yield return new HotkeyBinding("note", Notes.Hotkey, Notes.Trigger, HotkeyAction.Note, Notes.ModeId);
        yield return new HotkeyBinding("library", Notes.LibraryHotkey, TriggerMode.Toggle, HotkeyAction.ToggleLibrary);
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

        BuiltInModes.EnsurePresent(Modes);
        if (FindMode(DefaultModeId) is null) DefaultModeId = BuiltInModes.VerbatimId;
        if (FindMode(Notes.ModeId) is null) Notes.ModeId = BuiltInModes.VerbatimId;
        foreach (var m in Modes) m.Temperature = Math.Clamp(m.Temperature, 0, 1.5);

        History.MaxEntries = Math.Clamp(History.MaxEntries, 0, 1000);
        History.MaxAgeDays = Math.Clamp(History.MaxAgeDays, 1, 365);
        Insertion.ClearClipboardAfterSeconds = Math.Clamp(Insertion.ClearClipboardAfterSeconds, 0, 3600);
        Insertion.RestoreDelayMs = Math.Clamp(Insertion.RestoreDelayMs, 100, 5000);
    }
}

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
    public KeyCombo Hotkey { get; set; } = new(KeyModifiers.Ctrl | KeyModifiers.Alt, VirtualKeys.Space);
    public TriggerMode Trigger { get; set; } = TriggerMode.Hold;
    public string ModeId { get; set; } = BuiltInModes.VerbatimId;
    public KeyCombo LibraryHotkey { get; set; } = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 'L');
    public bool CopyToClipboard { get; set; } = true;
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
