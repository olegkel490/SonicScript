using SonicScript.Core.Hotkeys;

namespace SonicScript.Core.Modes;

public enum ModeKind
{
    /// <summary>Nur Transkription, kein Sprachmodell.</summary>
    Verbatim,
    /// <summary>Sauberer Schrifttext.</summary>
    Improve,
    /// <summary>Frust → ruhige, wirksame Nachricht.</summary>
    Calm,
    /// <summary>Text mit passenden Emojis.</summary>
    Emoji,
    /// <summary>Eigener Modus mit eigener Anweisung.</summary>
    Custom,
    /// <summary>Übersetzung ins Englische.</summary>
    Translate,
    /// <summary>Gesprochener Gedanke → klarer Prompt für ChatGPT, Claude, Cursor …</summary>
    Prompt,
}

public sealed class ModeDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ModeKind Kind { get; set; } = ModeKind.Custom;
    public string Name { get; set; } = "";
    public string Symbol { get; set; } = "✦";

    /// <summary>Eigene Anweisung. Bei eingebauten Modi optional (leer = Standard-Prompt).</summary>
    public string Instruction { get; set; } = "";

    public double Temperature { get; set; } = 0.3;
    public KeyCombo Hotkey { get; set; } = KeyCombo.None;
    public TriggerMode Trigger { get; set; } = TriggerMode.Hold;

    /// <summary>
    /// Sprachbefehle, kommagetrennt: Beginnt ein Diktat mit "Stichwort:" (bzw. Komma/Punkt), gilt dieser Modus.
    /// Leer bei eigenen Modi = der Name.
    /// </summary>
    public string Keywords { get; set; } = "";

    /// <summary>Im Moduswechsel (Strg+Alt+. / Strg+Alt+-) enthalten.</summary>
    public bool InCycle { get; set; } = true;

    public bool IsBuiltIn => Kind != ModeKind.Custom;
    public bool NeedsLanguageModel => Kind != ModeKind.Verbatim;

    public ModeDefinition Clone() => (ModeDefinition)MemberwiseClone();
}

public static class BuiltInModes
{
    public const string VerbatimId = "verbatim";
    public const string ImproveId = "improve";
    public const string CalmId = "calm";
    public const string EmojiId = "emoji";
    public const string TranslateId = "translate";
    public const string PromptId = "prompt";

    public static List<ModeDefinition> CreateDefaults() =>
    [
        new() { Id = VerbatimId, Kind = ModeKind.Verbatim, Name = "SonicScript", Symbol = "◉", Temperature = 0,
                Keywords = DefaultKeywords[VerbatimId] },
        new() { Id = ImproveId, Kind = ModeKind.Improve, Name = "SonicScript+", Symbol = "✎", Temperature = 0.3,
                Keywords = DefaultKeywords[ImproveId] },
        new() { Id = CalmId, Kind = ModeKind.Calm, Name = "SonicScript $%&!", Symbol = "☯", Temperature = 0.4,
                Keywords = DefaultKeywords[CalmId] },
        new() { Id = EmojiId, Kind = ModeKind.Emoji, Name = "SonicScript :)", Symbol = "☺", Temperature = 0.5,
                Keywords = DefaultKeywords[EmojiId] },
        new() { Id = TranslateId, Kind = ModeKind.Translate, Name = "SonicScript EN", Symbol = "EN", Temperature = 0.2,
                Keywords = DefaultKeywords[TranslateId] },
        new() { Id = PromptId, Kind = ModeKind.Prompt, Name = "SonicScript Prompt", Symbol = "»", Temperature = 0.3,
                Keywords = DefaultKeywords[PromptId] },
    ];

    /// <summary>Standard-Sprachbefehle der eingebauten Modi.</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultKeywords = new Dictionary<string, string>
    {
        [VerbatimId] = "Wörtlich, Wortgetreu, Verbatim",
        [ImproveId] = "Sauber, Plus, Clean",
        [CalmId] = "Ruhig, Sachlich, Calm",
        [EmojiId] = "Emoji, Emojis",
        [TranslateId] = "Englisch, Auf Englisch, English, Übersetze, Übersetzen",
        [PromptId] = "Prompt",
    };

    /// <summary>Standardtasten von Schema 2/3, die mit Schema 4 entfallen (Modi per Sprachbefehl).</summary>
    internal static readonly Dictionary<string, KeyCombo> Schema3Hotkeys = new()
    {
        [CalmId] = new KeyCombo(KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeys.OemPeriod),
        [EmojiId] = new KeyCombo(KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeys.OemComma),
        [TranslateId] = new KeyCombo(KeyModifiers.Ctrl, VirtualKeys.OemMinus),
    };

    /// <summary>Standardtasten bis Schema 1 (für die Migration auf die neuen Tasten).</summary>
    internal static readonly Dictionary<string, KeyCombo> LegacyHotkeys = new()
    {
        [ImproveId] = new KeyCombo(KeyModifiers.Ctrl | KeyModifiers.Win | KeyModifiers.Shift, 0),
        [CalmId] = new KeyCombo(KeyModifiers.Ctrl | KeyModifiers.Win | KeyModifiers.Alt, 0),
        [EmojiId] = new KeyCombo(KeyModifiers.Alt | KeyModifiers.Win, 0),
        [TranslateId] = new KeyCombo(KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Win, 0),
    };

    /// <summary>Fehlende eingebaute Modi ergänzen (z. B. nach manueller Bearbeitung der settings.json).</summary>
    public static void EnsurePresent(IList<ModeDefinition> modes)
    {
        foreach (var def in CreateDefaults())
        {
            if (!modes.Any(m => m.Id == def.Id))
            {
                // Taste nur übernehmen, wenn sie noch frei ist (z. B. neuer eingebauter Modus nach einem Update).
                if (modes.Any(m => m.Hotkey == def.Hotkey)) def.Hotkey = KeyCombo.None;
                modes.Insert(Math.Min(modes.Count, CreateDefaults().FindIndex(d => d.Id == def.Id)), def);
            }
        }
    }
}
