using System.Diagnostics;
using System.Runtime.InteropServices;
using SonicScript.Core.Hotkeys;
using static SonicScript.Platform.Windows.NativeMethods;

namespace SonicScript.Platform.Windows;

/// <summary>
/// Globaler Low-Level-Tastatur-Hook. Muss auf einem Thread mit Nachrichtenschleife installiert werden (UI-Thread).
/// Die Logik steckt im plattformneutralen <see cref="HotkeyMatcher"/>; Ereignisse werden über
/// <see cref="HotkeyTriggered"/> gemeldet und sollten schnell weitergereicht werden (Windows entfernt langsame Hooks).
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private readonly LowLevelKeyboardProc _proc; // Referenz halten, sonst sammelt der GC den Delegate ein
    private IntPtr _hook;

    public KeyboardHook()
    {
        _proc = HookCallback;
    }

    /// <summary>Austauschbar, z. B. nach Änderung der Tastenbelegung.</summary>
    public HotkeyMatcher? Matcher { get; set; }

    public event Action<HotkeyEvent>? HotkeyTriggered;

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        using var module = Process.GetCurrentProcess().MainModule;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module?.ModuleName), 0);
        if (_hook == IntPtr.Zero) throw new InvalidOperationException("SetWindowsHookEx failed: " + Marshal.GetLastWin32Error());
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Matcher is { } matcher)
        {
            try
            {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                // Selbst erzeugte Tasten (eigenes Strg+V, getippter Text) ignorieren.
                if ((info.flags & LLKHF_INJECTED) == 0)
                {
                    var msg = (int)wParam;
                    var down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
                    var up = msg is WM_KEYUP or WM_SYSKEYUP;
                    if (down || up)
                    {
                        var result = matcher.OnKey((int)info.vkCode, down);
                        if (result.InjectMenuMask)
                        {
                            // Verhindert, dass das Loslassen von Win das Startmenü öffnet.
                            Send(Key(VirtualKeys.MenuMask, false), Key(VirtualKeys.MenuMask, true));
                        }
                        if (result.Event is { } ev) HotkeyTriggered?.Invoke(ev);
                        if (result.Swallow) return 1;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("KeyboardHook: " + ex);
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
