using System.Text.Json;
using System.Text.Json.Serialization;
using SonicScript.Core.Storage;

namespace SonicScript.Core.Settings;

public sealed class SettingsStore(string path)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // lesbare Datei: "Ctrl+Win" statt "Ctrl\u002BWin", Umlaute unverändert
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Path { get; } = path;

    /// <summary>Lädt die Einstellungen. Fehlt die Datei oder ist sie kaputt, gibt es Standardwerte (kaputte Datei wird gesichert).</summary>
    public AppSettings Load()
    {
        AppSettings? s = null;
        if (File.Exists(Path))
        {
            try
            {
                s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), JsonOptions);
            }
            catch (JsonException)
            {
                File.Copy(Path, Path + ".broken", overwrite: true);
            }
        }
        s ??= new AppSettings();
        s.Normalize();
        return s;
    }

    public void Save(AppSettings settings) =>
        AtomicFile.WriteAllText(Path, JsonSerializer.Serialize(settings, JsonOptions));
}
