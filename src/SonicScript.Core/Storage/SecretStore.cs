using System.Text;

namespace SonicScript.Core.Storage;

/// <summary>Speichert Geheimnisse (API-Schlüssel) verschlüsselt als einzelne Dateien. Klartext landet nie in settings.json.</summary>
public sealed class SecretStore(string directory, ISecretProtector protector)
{
    private string PathFor(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(".."))
            throw new ArgumentException("Ungültiger Name für ein Geheimnis.", nameof(name));
        return Path.Combine(directory, name + ".secret");
    }

    public void Save(string name, string? value)
    {
        var path = PathFor(name);
        if (string.IsNullOrEmpty(value))
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        AtomicFile.WriteAllBytes(path, protector.Protect(Encoding.UTF8.GetBytes(value)));
    }

    public string Load(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var path = PathFor(name);
        if (!File.Exists(path)) return "";
        try
        {
            return Encoding.UTF8.GetString(protector.Unprotect(File.ReadAllBytes(path)));
        }
        catch (Exception)
        {
            // z. B. anderer Windows-Benutzer oder beschädigte Datei → wie "nicht gesetzt" behandeln
            return "";
        }
    }

    public bool Has(string name) => Load(name).Length > 0;
}
