using Avalonia;

namespace SonicScript.App;

internal static class Program
{
    private const string MutexName = @"Local\SonicScript.SingleInstance";
    private const string ActivateEventName = @"Local\SonicScript.Activate";

    [STAThread]
    public static int Main(string[] args)
    {
        // Nur eine Instanz. Ein zweiter Start (z. B. Doppelklick, während SonicScript schon im Tray läuft)
        // bittet die laufende Instanz, ihr Fenster zu zeigen, statt still zu verschwinden.
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirst);
        if (!isFirst)
        {
            try
            {
                using var activate = EventWaitHandle.OpenExisting(ActivateEventName);
                activate.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            return 0;
        }

        AppPaths.EnsureCreated();
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("Unobserved task", e.Exception); e.SetObserved(); };

        using var activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        var listener = new Thread(() =>
        {
            while (activateEvent.WaitOne()) ActivationRequested?.Invoke();
        }) { IsBackground = true, Name = "SonicScript.Activate" };
        listener.Start();

        StartInBackground = args.Contains("--background");
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static bool StartInBackground { get; private set; }

    /// <summary>Ein zweiter Start wurde versucht (kommt auf einem Hintergrund-Thread).</summary>
    public static event Action? ActivationRequested;

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Software-Rendering: Die wenigen kleinen Fenster brauchen keine GPU, und ANGLE/DirectX zeichnet auf
            // manchen Rechnern (VMs, Remotedesktop, ältere Treiber) nur schwarze/unsichtbare Fenster.
            .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
            .WithInterFont()
            .LogToTrace();
}
