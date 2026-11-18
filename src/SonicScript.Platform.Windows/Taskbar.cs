using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SonicScript.Platform.Windows;

public enum TaskbarEdge
{
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
}

/// <summary>Rechteck in physischen Pixeln.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>Lage der Windows-Taskleiste (Hauptbildschirm) und des Infobereichs ("Tray").</summary>
public sealed record TaskbarInfo(PixelRect Bounds, PixelRect? NotifyArea, TaskbarEdge Edge, bool AutoHide, bool LightTheme)
{
    public bool IsHorizontal => Edge is TaskbarEdge.Top or TaskbarEdge.Bottom;
}

public static class Taskbar
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const uint ABM_GETSTATE = 0x4;
    private const uint ABM_GETTASKBARPOS = 0x5;
    private const int ABS_AUTOHIDE = 0x1;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("shell32.dll")]
    private static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, char[] className, int maxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

    private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc proc, uint pid, uint tid, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    /// <summary>
    /// Meldet jeden Wechsel des Vordergrundfensters (auch Klicks auf die Taskleiste), damit das Modus-Feld sofort
    /// wieder über die Taskleiste gelegt werden kann. Muss auf einem Thread mit Nachrichtenschleife (UI) angelegt werden.
    /// </summary>
    public sealed class ForegroundWatcher : IDisposable
    {
        private readonly WinEventProc _proc; // Referenz halten (GC)
        private IntPtr _hook;

        public ForegroundWatcher(Action onChange)
        {
            // Eine Exception darf nie zurück in Windows laufen – das beendet den Prozess sofort (FailFast).
            _proc = (_, _, _, _, _, _, _) =>
            {
                try { onChange(); }
                catch (Exception ex) { PlatformErrors.Report?.Invoke("Foreground watcher", ex); }
            };
            _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    public static TaskbarInfo? Get()
    {
        try
        {
            var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
            if (SHAppBarMessage(ABM_GETTASKBARPOS, ref data) == UIntPtr.Zero) return null;
            var bounds = new PixelRect(data.rc.Left, data.rc.Top, data.rc.Right, data.rc.Bottom);
            var state = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
            var autoHide = ((uint)SHAppBarMessage(ABM_GETSTATE, ref state) & ABS_AUTOHIDE) != 0;

            PixelRect? notify = null;
            var tray = FindWindow("Shell_TrayWnd", null);
            if (tray != IntPtr.Zero)
            {
                var n = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
                if (n != IntPtr.Zero && GetWindowRect(n, out var r) && r.Right > r.Left)
                    notify = new PixelRect(r.Left, r.Top, r.Right, r.Bottom);
            }
            return new TaskbarInfo(bounds, notify, (TaskbarEdge)Math.Min(data.uEdge, 3u), autoHide, SystemUsesLightTheme());
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Hell/Dunkel der Taskleiste (kann vom App-Design abweichen).</summary>
    public static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Vordergrundfenster füllt seinen Bildschirm komplett (Video, Präsentation, Spiel).</summary>
    public static bool IsForegroundFullscreen()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        var cls = new char[64];
        var n = GetClassName(fg, cls, cls.Length);
        var name = n > 0 ? new string(cls, 0, n) : "";
        if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        if (!GetWindowRect(fg, out var r)) return false;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST), ref info)) return false;
        var m = info.rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    /// <summary>Die Taskleiste schiebt sich bei Klick nach vorn – das Abzeichen danach wieder darüber legen.</summary>
    public static void BringToTop(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
