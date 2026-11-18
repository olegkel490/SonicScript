using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SonicScript.App.Services;

namespace SonicScript.App;

public sealed partial class App : Application
{
    private AppController? _controller;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Fehler in Ereignissen/Fensterprozeduren loggen statt die App still zu beenden.
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                Log.Error("UI thread", e.Exception);
                e.Handled = true;
            };
            SonicScript.Platform.Windows.PlatformErrors.Report = Log.Error;
            try
            {
                _controller = new AppController(desktop);
                desktop.Exit += (_, e) =>
                {
                    try { _controller.Dispose(); }
                    catch (Exception ex) { Log.Error("Dispose", ex); }
                    finally { CrashGuard.Stop(_controller.ExitReason ?? $"code {e.ApplicationExitCode}"); }
                };
                _controller.Start();
                Program.ActivationRequested += () =>
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => _controller.ShowMainWindow());
            }
            catch (Exception ex)
            {
                // Ohne diese Meldung würde die App beim Start still verschwinden.
                Log.Error("Startup failed", ex);
                Views.StartupError.Show(ex, desktop);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
