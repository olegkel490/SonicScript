using SonicScript.Core.Hotkeys;
using SonicScript.Core.Settings;
using static SonicScript.Platform.Windows.NativeMethods;

namespace SonicScript.Platform.Windows;

public enum InsertOutcome
{
    Inserted,
    Typed,
    /// <summary>Nur in der Zwischenablage (gewollt, Fokus gewechselt, Passwortfeld, Tasten noch gedrückt).</summary>
    ClipboardOnly,
    /// <summary>Eingabe vom System blockiert (z. B. Ziel läuft als Administrator) – Text liegt in der Zwischenablage.</summary>
    Blocked,
    Failed,
}

/// <summary>
/// Fügt Text am Cursor ein: Zwischenablage + Strg+V (Standard), Strg+Shift+V oder simuliertes Tippen.
/// Stellt die vorherige Zwischenablage danach wieder her, wenn sich zwischenzeitlich nichts geändert hat.
/// </summary>
public sealed class TextInserter
{
    public async Task<InsertOutcome> InsertAsync(string text, InsertionMethod method, ForegroundInfo target, InsertionSettings settings)
    {
        if (string.IsNullOrEmpty(text)) return InsertOutcome.Failed;

        // Fokus gewechselt oder Passwortfeld → nichts tippen/einfügen, nur Zwischenablage.
        var focusChanged = target.Handle != IntPtr.Zero && ForegroundWindow.Current != target.Handle;
        if (method == InsertionMethod.ClipboardOnly || focusChanged || target.IsPasswordField)
            return await ClipboardOnlyAsync(text, settings).ConfigureAwait(true);

        if (!await WaitForModifiersReleasedAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(true))
            return await ClipboardOnlyAsync(text, settings).ConfigureAwait(true);

        if (method == InsertionMethod.Type)
            return await TypeAsync(text).ConfigureAwait(true);

        var snapshot = settings.RestoreClipboard ? ClipboardService.Capture() : null;
        if (!await ClipboardService.SetTextAsync(text).ConfigureAwait(true))
            return settings.TypeAsFallback ? await TypeAsync(text).ConfigureAwait(true) : InsertOutcome.Failed;
        var ourSequence = ClipboardService.SequenceNumber;

        var sent = method == InsertionMethod.PasteShift
            ? Send(Key(VK_CONTROL, false), Key(VK_SHIFT, false), Key(VK_V, false), Key(VK_V, true), Key(VK_SHIFT, true), Key(VK_CONTROL, true))
            : Send(Key(VK_CONTROL, false), Key(VK_V, false), Key(VK_V, true), Key(VK_CONTROL, true));
        if (sent == 0) return InsertOutcome.Blocked; // Text bleibt in der Zwischenablage

        if (snapshot is not null)
            _ = RestoreLaterAsync(snapshot, ourSequence, settings.RestoreDelayMs);
        else if (settings.ClearClipboardAfterSeconds > 0)
            _ = ClearLaterAsync(ourSequence, settings.ClearClipboardAfterSeconds);

        return InsertOutcome.Inserted;
    }

    private static async Task<InsertOutcome> ClipboardOnlyAsync(string text, InsertionSettings settings)
    {
        if (!await ClipboardService.SetTextAsync(text).ConfigureAwait(true)) return InsertOutcome.Failed;
        if (settings.ClearClipboardAfterSeconds > 0)
            _ = ClearLaterAsync(ClipboardService.SequenceNumber, settings.ClearClipboardAfterSeconds);
        return InsertOutcome.ClipboardOnly;
    }

    /// <summary>Unicode-Tastendrücke in kleinen Blöcken; \n als Enter, \t als Tab.</summary>
    public static async Task<InsertOutcome> TypeAsync(string text)
    {
        var batch = new List<INPUT>(128);
        foreach (var c in text.Replace("\r\n", "\n"))
        {
            switch (c)
            {
                case '\r':
                    continue;
                case '\n':
                    batch.Add(Key(VK_RETURN, false));
                    batch.Add(Key(VK_RETURN, true));
                    break;
                case '\t':
                    batch.Add(Key(VK_TAB, false));
                    batch.Add(Key(VK_TAB, true));
                    break;
                default:
                    batch.Add(Unicode(c, false));
                    batch.Add(Unicode(c, true));
                    break;
            }
            if (batch.Count >= 96)
            {
                if (Send([.. batch]) == 0) return InsertOutcome.Blocked;
                batch.Clear();
                await Task.Delay(8).ConfigureAwait(true);
            }
        }
        if (batch.Count > 0 && Send([.. batch]) == 0) return InsertOutcome.Blocked;
        return InsertOutcome.Typed;
    }

    private static async Task RestoreLaterAsync(ClipboardService.Snapshot snapshot, uint ourSequence, int delayMs)
    {
        await Task.Delay(delayMs).ConfigureAwait(true);
        // Nur zurücksetzen, wenn niemand die Zwischenablage inzwischen geändert hat.
        if (ClipboardService.SequenceNumber != ourSequence) return;
        if (snapshot.IsEmpty) ClipboardService.Clear();
        else ClipboardService.Restore(snapshot);
    }

    private static async Task ClearLaterAsync(uint ourSequence, int seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(true);
        if (ClipboardService.SequenceNumber == ourSequence) ClipboardService.Clear();
    }

    private static async Task<bool> WaitForModifiersReleasedAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!AnyModifierDown()) return true;
            await Task.Delay(25).ConfigureAwait(true);
        }
        return !AnyModifierDown();
    }

    private static bool AnyModifierDown() =>
        new[] { VirtualKeys.Shift, VirtualKeys.Control, VirtualKeys.Menu, VirtualKeys.LWin, VirtualKeys.RWin }
            .Any(vk => (GetAsyncKeyState(vk) & 0x8000) != 0);

    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_V = 0x56;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_TAB = 0x09;
}
