using Avalonia;

namespace SonicScript.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Nur eine Instanz: ein zweiter Start öffnet nichts Neues.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\SonicScript.SingleInstance", out var isFirst);
        if (!isFirst) return 0;

        AppPaths.EnsureCreated();
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("Unobserved task", e.Exception); e.SetObserved(); };

        StartInBackground = args.Contains("--background");
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static bool StartInBackground { get; private set; }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
