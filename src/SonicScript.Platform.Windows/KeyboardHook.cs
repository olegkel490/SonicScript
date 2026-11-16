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
                        if (result.Replay is { } combo) Replay(combo);
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

    /// <summary>
    /// Kombination an die Ziel-App nachreichen (kurz angetippt = normale Funktion). Bereits losgelassene
    /// Modifikatoren werden für die Dauer des Tastendrucks kurz mitgedrückt. Injizierte Tasten ignoriert der Hook.
    /// </summary>
    private static void Replay(KeyCombo combo)
    {
        var mods = new List<ushort>();
        void Need(KeyModifiers flag, int generic, int left)
        {
            if (combo.Modifiers.HasFlag(flag) && (GetAsyncKeyState(generic) & 0x8000) == 0) mods.Add((ushort)left);
        }
        Need(KeyModifiers.Ctrl, VirtualKeys.Control, VirtualKeys.LControl);
        Need(KeyModifiers.Shift, VirtualKeys.Shift, VirtualKeys.LShift);
        Need(KeyModifiers.Alt, VirtualKeys.Menu, VirtualKeys.LMenu);
        if (combo.Modifiers.HasFlag(KeyModifiers.Win) &&
            (GetAsyncKeyState(VirtualKeys.LWin) & 0x8000) == 0 && (GetAsyncKeyState(VirtualKeys.RWin) & 0x8000) == 0)
            mods.Add(VirtualKeys.LWin);

        var inputs = new List<INPUT>();
        inputs.AddRange(mods.Select(m => Key(m, false)));
        inputs.Add(Key((ushort)combo.Key, false));
        inputs.Add(Key((ushort)combo.Key, true));
        inputs.AddRange(mods.AsEnumerable().Reverse().Select(m => Key(m, true)));
        Send([.. inputs]);
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
