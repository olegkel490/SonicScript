using SonicScript.Core.Rules;
using static SonicScript.Platform.Windows.NativeMethods;

namespace SonicScript.Platform.Windows;

public sealed record ForegroundInfo(IntPtr Handle, ForegroundApp App, bool IsPasswordField);

public static class ForegroundWindow
{
    public static IntPtr Current => GetForegroundWindow();

    public static ForegroundInfo Capture()
    {
        var hwnd = GetForegroundWindow();
        var threadId = GetWindowThreadProcessId(hwnd, out var pid);
        return new ForegroundInfo(hwnd, new ForegroundApp(ProcessName(pid), Title(hwnd)), IsPasswordFocused(threadId));
    }

    private static string Title(IntPtr hwnd)
    {
        var buf = new char[512];
        var n = GetWindowText(hwnd, buf, buf.Length);
        return n > 0 ? new string(buf, 0, n) : "";
    }

    /// <summary>Funktioniert auch bei Prozessen mit höheren Rechten (Process.GetProcessById würde scheitern).</summary>
    private static string ProcessName(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return "";
        try
        {
            var buf = new char[1024];
            var size = buf.Length;
            return QueryFullProcessImageName(h, 0, buf, ref size)
                ? Path.GetFileNameWithoutExtension(new string(buf, 0, size))
                : "";
        }
        finally { CloseHandle(h); }
    }

    /// <summary>
    /// Erkennt klassische Win32-Passwortfelder (Edit mit ES_PASSWORD). Browser- und XAML-Felder werden so nicht
    /// erkannt (dafür wäre UI Automation nötig – geplant).
    /// </summary>
    private static bool IsPasswordFocused(uint threadId)
    {
        var gui = new GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
        if (!GetGUIThreadInfo(threadId, ref gui) || gui.hwndFocus == IntPtr.Zero) return false;
        var cls = new char[64];
        var n = GetClassName(gui.hwndFocus, cls, cls.Length);
        var name = n > 0 ? new string(cls, 0, n) : "";
        if (!name.Contains("Edit", StringComparison.OrdinalIgnoreCase)) return false;
        var style = (long)GetWindowLongPtr(gui.hwndFocus, GWL_STYLE);
        return (style & ES_PASSWORD) != 0;
    }
}
