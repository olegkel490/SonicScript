using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using SonicScript.App.Views;
using SonicScript.Core.Settings;

[assembly: AvaloniaTestApplication(typeof(SonicScript.App.Tests.TestAppBuilder))]

namespace SonicScript.App.Tests;

public sealed class TestApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>
/// Startet die Fenster der App ohne Bildschirm (Avalonia Headless) und klickt sie durch. Fängt XAML-Fehler,
/// fehlende Ressourcen und Abstürze beim Anzeigen ab, bevor ein Release rausgeht.
/// </summary>
public class UiTests
{
    [AvaloniaFact]
    public void SettingsWindowOpensAndAllTabsRender()
    {
        var w = new SettingsWindow(null!);
        w.Show();
        var tabs = w.FindControl<TabControl>("Tabs")!;
        Assert.True(tabs.ItemCount >= 10);
        for (var i = 0; i < tabs.ItemCount; i++)
        {
            tabs.SelectedIndex = i;
            Dispatcher.UIThread.RunJobs();
        }
        foreach (var tab in new[] { "general", "dictation", "speech", "modes", "apps", "notes", "dictionary", "stats", "diagnose", "history" })
            w.SelectTab(tab);
        w.Close();
    }

    [AvaloniaFact]
    public void ModeBadgeShowsEveryCycleEntry()
    {
        var s = new AppSettings();
        var badge = new ModeBadge();
        badge.Show();
        foreach (var entry in s.BuildCycle())
        {
            badge.Update(entry, "idle");
            badge.Update(entry, "rec");
            Dispatcher.UIThread.RunJobs();
        }
        badge.Hide();
        badge.Show(); // nach Vollbild wieder einblenden
        badge.Close();
    }

    [AvaloniaFact]
    public void RecordingIndicatorHandlesPreviewAndProcessing()
    {
        var s = new AppSettings();
        var ind = new RecordingIndicator();
        ind.ShowRecording(s.DefaultMode, false);
        ind.SetLevel(0.5f);
        ind.SetPreview("Das ist eine sehr lange Vorschau " + new string('x', 400));
        ind.SetPreview(null);
        ind.ShowProcessing();
        Dispatcher.UIThread.RunJobs();
        ind.HideIndicator();
    }

    [AvaloniaFact]
    public void ModeSwitchOsdShowsEntry()
    {
        var s = new AppSettings();
        var osd = new ModeSwitchOsd();
        var cycle = s.BuildCycle();
        osd.ShowEntry(cycle[2], cycle, "Strg+Alt+.", "Strg+Alt+-");
        Dispatcher.UIThread.RunJobs();
        osd.Close();
    }

    [AvaloniaFact]
    public void ToastWithButtonsRuns()
    {
        var clicked = false;
        Toast.Show("Test", false, "OK", () => clicked = true, "Nein", null);
        Toast.Show("Fehler", isError: true);
        Dispatcher.UIThread.RunJobs();
        Assert.False(clicked);
    }

    [AvaloniaFact]
    public void LibraryWindowOpensWithNotes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ss-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var s = new AppSettings();
            s.Notes.Folder = dir;
            new Core.Notes.NoteStore(dir).Append(DateTime.Now, "Testnotiz");
            var w = new LibraryWindow(s);
            w.Show();
            Dispatcher.UIThread.RunJobs();
            w.Close();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SyncedFolderIsDetected()
    {
        Assert.True(AppLocation.IsSyncedFolder(@"C:\Users\a\OneDrive - Firma\Apps\SonicScript.exe"));
        Assert.True(AppLocation.IsSyncedFolder(@"C:\Users\a\Dropbox\SonicScript.exe"));
        Assert.False(AppLocation.IsSyncedFolder(@"C:\Users\a\AppData\Local\Programs\SonicScript\SonicScript.exe"));
    }
}
