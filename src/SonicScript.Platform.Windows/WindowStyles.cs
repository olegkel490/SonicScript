using System.Runtime.InteropServices;

namespace SonicScript.Platform.Windows;

public static class WindowStyles
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    private const long WS_EX_TOPMOST = 0x00000008;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

    /// <summary>Overlay, das nie den Fokus nimmt (Text landet weiter im Zielfenster) und nicht in Alt+Tab erscheint.</summary>
    public static void MakeOverlay(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        var ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)(ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWCP_ROUNDSMALL = 3;

    /// <summary>Windows 11 rundet Fenster selbst ab (mit Rahmen und Schatten wie bei Systemmenüs).</summary>
    public static bool HasNativeRoundedCorners => Environment.OSVersion.Version.Build >= 22000;

    /// <summary>
    /// Abgerundete Fensterecken ohne Transparenz: Windows 11 über DWM (8 px, mit Schatten),
    /// ältere Versionen über eine Fensterregion. Bei Größenänderung erneut aufrufen.
    /// </summary>
    public static void RoundCorners(IntPtr hwnd, int widthPx, int heightPx, int radiusPx, bool small = false)
    {
        if (hwnd == IntPtr.Zero || widthPx <= 0 || heightPx <= 0) return;
        if (HasNativeRoundedCorners)
        {
            var pref = small ? DWMWCP_ROUNDSMALL : DWMWCP_ROUND;
            if (DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)) == 0) return;
        }
        // Region gehört nach SetWindowRgn dem System – nicht selbst freigeben.
        var rgn = CreateRoundRectRgn(0, 0, widthPx + 1, heightPx + 1, radiusPx * 2, radiusPx * 2);
        if (rgn != IntPtr.Zero) SetWindowRgn(hwnd, rgn, true);
    }

    /// <summary>Hinweis auf vorhandene Vulkan-Laufzeit (Grafiktreiber) – für die erste Modell-Empfehlung.</summary>
    public static bool HasVulkanRuntime =>
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "vulkan-1.dll"));
}
