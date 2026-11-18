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
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT point);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

    private const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    private const long WS_CHILD = 0x40000000, WS_POPUP = 0x80000000, WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000;
    private const long WS_EX_TOPMOST = 0x8, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;

    /// <summary>Hauptfenster der Taskleiste (Hauptbildschirm).</summary>
    public static IntPtr TrayWindow => FindWindow("Shell_TrayWnd", null);

    public static bool IsAlive(IntPtr hwnd) => hwnd != IntPtr.Zero && IsWindow(hwnd);

    /// <summary>
    /// Fenster als Kindfenster in die Taskleiste einbetten (wie Taskleisten-Tools es tun): bleibt beim Klick auf die
    /// Taskleiste sichtbar, bewegt sich mit ihr und verschwindet mit ihr bei Vollbild. Startet Explorer neu, wird das
    /// Fenster mit zerstört – der Aufrufer muss es dann neu anlegen.
    /// </summary>
    public static bool Embed(IntPtr hwnd, IntPtr tray)
    {
        if (!IsAlive(hwnd) || !IsAlive(tray)) return false;
        var style = (long)GetWindowLongPtr(hwnd, GWL_STYLE);
        style = (style & ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME)) | WS_CHILD;
        SetWindowLongPtr(hwnd, GWL_STYLE, (IntPtr)style);
        var ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        ex = (ex & ~(WS_EX_TOPMOST | WS_EX_APPWINDOW)) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)ex);
        SetParent(hwnd, tray);
        return GetParent(hwnd) == tray;
    }

    /// <summary>Kindfenster an eine Bildschirmposition (Pixel) innerhalb der Taskleiste setzen und nach vorn holen.</summary>
    public static void PlaceChild(IntPtr hwnd, IntPtr tray, int screenX, int screenY, int width, int height)
    {
        var p = new POINT { X = screenX, Y = screenY };
        ScreenToClient(tray, ref p);
        SetWindowPos(hwnd, HWND_TOP, p.X, p.Y, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
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
