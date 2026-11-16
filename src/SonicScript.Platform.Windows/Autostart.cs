using Microsoft.Win32;

namespace SonicScript.Platform.Windows;

/// <summary>Autostart über HKCU\…\Run (keine Adminrechte nötig).</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SonicScript";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("no_process_path");
            key.SetValue(ValueName, $"\"{exe}\" --background");
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName);
        }
    }
}
