using SonicScript.Core.Hotkeys;
using SonicScript.Core.Settings;

namespace SonicScript.App;

/// <summary>Auswahllisten für ComboBoxen in XAML (<c>{x:Static l:Choices.…}</c>).</summary>
public static class Choices
{
    public static TriggerMode[] TriggerModes { get; } = Enum.GetValues<TriggerMode>();
    public static InsertionMethod[] InsertionMethods { get; } = Enum.GetValues<InsertionMethod>();
    public static TranscriptionEngine[] Engines { get; } = Enum.GetValues<TranscriptionEngine>();
    public static ProviderPreset[] Providers { get; } = Enum.GetValues<ProviderPreset>();
    public static ProviderPreset[] TranscriptionProviders { get; } = [ProviderPreset.OpenRouter, ProviderPreset.OpenAI, ProviderPreset.Custom];
    public static DockSide[] DockSides { get; } = Enum.GetValues<DockSide>();
    public static string[] UiLanguages { get; } = ["auto", "de", "en"];
    public static string[] SpeechLanguages { get; } = ["auto", "de", "en", "fr", "es", "it", "nl", "pl", "pt", "tr", "uk", "ru"];
}
