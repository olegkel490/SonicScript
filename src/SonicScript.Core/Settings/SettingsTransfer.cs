using System.Text.Json;
using System.Text.Json.Nodes;

namespace SonicScript.Core.Settings;

/// <summary>
/// Einstellungen zwischen Rechnern übertragen. Exportiert wird alles außer gerätespezifischen Werten und
/// API-Schlüsseln (die sind per Windows-DPAPI an Benutzer und Rechner gebunden und müssen neu eingegeben werden).
/// </summary>
public static class SettingsTransfer
{
    public const string Marker = "SonicScript-Einstellungen";

    public static string Export(AppSettings settings, string appVersion)
    {
        var node = JsonSerializer.SerializeToNode(settings, SettingsStore.JsonOptions)!.AsObject();
        foreach (var key in DeviceSpecificTopLevel) node.Remove(key);
        if (node["transcription"] is JsonObject t)
        {
            t.Remove("microphoneDevice");
            t.Remove("localModelId");
            t.Remove("useGpu");
        }
        var wrapper = new JsonObject
        {
            ["format"] = Marker,
            ["version"] = 1,
            ["appVersion"] = appVersion,
            ["exportedAt"] = DateTimeOffset.Now.ToString("O"),
            ["settings"] = node,
        };
        return wrapper.ToJsonString(SettingsStore.JsonOptions);
    }

    /// <summary>Werte, die zu diesem Rechner gehören und beim Import erhalten bleiben.</summary>
    private static readonly string[] DeviceSpecificTopLevel =
        ["firstRunCompleted", "autostart", "modeBadgeX", "modeBadgeY"];

    /// <summary>
    /// Liest eine Exportdatei und übernimmt sie in eine neue Einstellung. Gerätespezifisches (Mikrofon, lokales Modell,
    /// Grafikkarte, Autostart, Einrichtung) kommt aus <paramref name="current"/>; ein Notizordner, den es hier nicht
    /// gibt, ebenfalls. Wirft <see cref="FormatException"/> bei einer ungültigen Datei.
    /// </summary>
    public static AppSettings Import(string json, AppSettings current, Func<string, bool>? directoryExists = null)
    {
        directoryExists ??= Directory.Exists;
        JsonNode? root;
        try { root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
        catch (JsonException ex) { throw new FormatException("invalid_json", ex); }

        // Exportdatei (mit Hülle) oder direkt eine settings.json
        var node = root is JsonObject r && r["format"] is JsonValue f && f.TryGetValue<string>(out var fmt) && fmt == Marker ? r["settings"] : root;
        if (node is not JsonObject obj) throw new FormatException("not_settings");

        AppSettings? imported;
        try { imported = obj.Deserialize<AppSettings>(SettingsStore.JsonOptions); }
        catch (JsonException ex) { throw new FormatException("invalid_settings", ex); }
        if (imported is null) throw new FormatException("not_settings");
        imported.Normalize();

        // Gerätespezifisches vom aktuellen Rechner behalten.
        imported.FirstRunCompleted = current.FirstRunCompleted;
        imported.Autostart = current.Autostart;
        imported.ModeBadgeX = current.ModeBadgeX;
        imported.ModeBadgeY = current.ModeBadgeY;
        imported.Transcription.MicrophoneDevice = current.Transcription.MicrophoneDevice;
        imported.Transcription.LocalModelId = current.Transcription.LocalModelId;
        imported.Transcription.UseGpu = current.Transcription.UseGpu;
        if (string.IsNullOrWhiteSpace(imported.Notes.Folder) || !directoryExists(imported.Notes.Folder))
            imported.Notes.Folder = current.Notes.Folder;
        return imported;
    }
}
