namespace SonicScript.App;

/// <summary>
/// %APPDATA%\SonicScript: settings.json, Geheimnisse, Verlauf.
/// %LOCALAPPDATA%\SonicScript: Modelle, laufende/fehlgeschlagene Aufnahmen, Log.
/// </summary>
public static class AppPaths
{
    public static string Roaming { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SonicScript");
    public static string Local { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SonicScript");

    public static string Settings => Path.Combine(Roaming, "settings.json");
    public static string Secrets => Path.Combine(Roaming, "secrets");
    public static string History => Path.Combine(Roaming, "history.bin");
    public static string Stats => Path.Combine(Roaming, "stats.json");
    public static string Reminders => Path.Combine(Roaming, "reminders.json");
    public static string Models => Path.Combine(Local, "models");
    public static string Recordings => Path.Combine(Local, "recordings");
    public static string Pending => Path.Combine(Local, "pending");
    public static string LogFile => Path.Combine(Local, "sonicscript.log");

    public static string DefaultNotesFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SonicScript Notizen");

    public static void EnsureCreated()
    {
        foreach (var d in new[] { Roaming, Local, Secrets, Models, Recordings, Pending })
            Directory.CreateDirectory(d);
    }
}
