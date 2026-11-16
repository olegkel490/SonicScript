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

    public static List<ModeDefinition> CreateDefaults() =>
    [
        new() { Id = VerbatimId, Kind = ModeKind.Verbatim, Name = "SonicScript", Symbol = "◉", Temperature = 0 },
        new() { Id = ImproveId, Kind = ModeKind.Improve, Name = "SonicScript+", Symbol = "✎", Temperature = 0.3,
                Hotkey = new KeyCombo(KeyModifiers.Ctrl | KeyModifiers.Win | KeyModifiers.Shift, 0) },
        new() { Id = CalmId, Kind = ModeKind.Calm, Name = "SonicScript $%&!", Symbol = "☯", Temperature = 0.4,
                Hotkey = new KeyCombo(KeyModifiers.Ctrl | KeyModifiers.Win | KeyModifiers.Alt, 0) },
        new() { Id = EmojiId, Kind = ModeKind.Emoji, Name = "SonicScript :)", Symbol = "☺", Temperature = 0.5,
                Hotkey = new KeyCombo(KeyModifiers.Alt | KeyModifiers.Win, 0) },
    ];

    /// <summary>Fehlende eingebaute Modi ergänzen (z. B. nach manueller Bearbeitung der settings.json).</summary>
    public static void EnsurePresent(List<ModeDefinition> modes)
    {
        foreach (var def in CreateDefaults())
        {
            if (!modes.Any(m => m.Id == def.Id))
                modes.Insert(Math.Min(modes.Count, CreateDefaults().FindIndex(d => d.Id == def.Id)), def);
        }
    }
}
