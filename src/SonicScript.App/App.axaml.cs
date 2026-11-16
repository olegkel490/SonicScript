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
            _controller = new AppController(desktop);
            desktop.Exit += (_, _) => _controller.Dispose();
            _controller.Start();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
