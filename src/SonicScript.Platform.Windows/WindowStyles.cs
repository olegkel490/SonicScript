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

    /// <summary>Hinweis auf vorhandene Vulkan-Laufzeit (Grafiktreiber) – für die erste Modell-Empfehlung.</summary>
    public static bool HasVulkanRuntime =>
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "vulkan-1.dll"));
}
