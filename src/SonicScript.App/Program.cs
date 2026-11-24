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
        using var mutex = new Mutex(initiallyOwned: false, MutexName);
        _mutex = mutex;
        // Nach einem Update läuft die alte Instanz evtl. noch kurz – bis zu 20 s auf sie warten.
        var afterUpdate = args.Contains(Services.Updater.AfterUpdateArg) || args.Contains(AppLocation.MovedArg);
        bool isFirst;
        try { isFirst = mutex.WaitOne(afterUpdate ? TimeSpan.FromSeconds(20) : TimeSpan.Zero); }
        catch (AbandonedMutexException) { isFirst = true; }
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

        AfterUpdate = args.Contains(Services.Updater.AfterUpdateArg);
        RolledBack = args.Contains(Services.Updater.RolledBackArg);
        var moved = Array.IndexOf(args, AppLocation.MovedArg);
        MovedFrom = moved >= 0 && moved + 1 < args.Length ? args[moved + 1] : null;
        StartInBackground = args.Contains("--background") || afterUpdate;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static bool StartInBackground { get; private set; }
    public static bool AfterUpdate { get; private set; }
    public static bool RolledBack { get; private set; }
    /// <summary>Alter Pfad, wenn SonicScript gerade aus OneDrive o. Ä. umgezogen ist.</summary>
    public static string? MovedFrom { get; private set; }

    private static Mutex? _mutex;

    /// <summary>Einzelinstanz-Sperre früh freigeben (Update/Umzug), damit die neue Instanz starten kann. Nur auf dem Hauptthread.</summary>
    public static void ReleaseSingleInstance()
    {
        try { _mutex?.ReleaseMutex(); }
        catch (ApplicationException) { }
        catch (ObjectDisposedException) { }
        _mutex = null;
    }

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
