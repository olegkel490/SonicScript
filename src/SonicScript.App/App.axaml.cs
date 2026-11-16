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
            try
            {
                _controller = new AppController(desktop);
                desktop.Exit += (_, _) => _controller.Dispose();
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
