using Interop.UIAutomationClient;

namespace SonicScript.Platform.Windows;

/// <summary>Was im fokussierten Eingabefeld steht – per UI Automation (funktioniert auch in Browsern, Office, XAML-Apps).</summary>
/// <param name="TextAfter">Text hinter dem Cursor (max. 10 000 Zeichen), z. B. die zitierte Mail im Antwortfenster.</param>
public sealed record FocusContext(bool IsPassword, string? TextBefore, string? SelectedText, string? TextAfter = null);

public static class FocusContextReader
{
    private const int UIA_TextPatternId = 10014;
    private const int UIA_ValuePatternId = 10002;
    private const int MaxBefore = 80;
    private const int MaxAfter = 10000;
    private const int MaxFull = 60000;

    /// <summary>
    /// Liest Passwort-Status, Text vor dem Cursor und markierten Text. Läuft auf einem eigenen Thread mit Zeitlimit,
    /// weil UI Automation bei hängenden Apps blockieren kann. null = nicht ermittelbar.
    /// </summary>
    public static async Task<FocusContext?> CaptureAsync(TimeSpan timeout)
    {
        var task = Task.Run(Capture);
        var done = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
        return done == task && task.IsCompletedSuccessfully ? task.Result : null;
    }

    /// <summary>Gesamter Text des fokussierten Feldes (für die Korrektur-Erkennung). null = nicht lesbar/Passwort.</summary>
    public static async Task<string?> ReadFocusedTextAsync(TimeSpan timeout)
    {
        var task = Task.Run(ReadFocusedText);
        var done = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
        return done == task && task.IsCompletedSuccessfully ? task.Result : null;
    }

    private static string? ReadFocusedText()
    {
        try
        {
            var uia = new CUIAutomation8Class();
            var el = uia.GetFocusedElement();
            if (el is null || el.CurrentIsPassword != 0) return null;
            if (el.GetCurrentPattern(UIA_TextPatternId) is IUIAutomationTextPattern text)
                return text.DocumentRange.GetText(MaxFull);
            if (el.GetCurrentPattern(UIA_ValuePatternId) is IUIAutomationValuePattern value)
                return value.CurrentValue;
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static FocusContext? Capture()
    {
        try
        {
            var uia = new CUIAutomation8Class();
            var el = uia.GetFocusedElement();
            if (el is null) return null;
            var isPassword = el.CurrentIsPassword != 0;
            if (isPassword) return new FocusContext(true, null, null);

            if (el.GetCurrentPattern(UIA_TextPatternId) is not IUIAutomationTextPattern text) return new FocusContext(false, null, null);

            var selection = text.GetSelection();
            if (selection is null || selection.Length == 0) return new FocusContext(false, null, null);
            var range = selection.GetElement(0);
            var selected = range.GetText(-1);

            var before = range.Clone();
            // Bereich auf "vom Feldanfang bis Auswahlanfang" setzen, dann auf die letzten Zeichen kürzen.
            before.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, range, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
            before.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, text.DocumentRange, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
            var full = before.GetText(-1) ?? "";
            var textBefore = full.Length > MaxBefore ? full[^MaxBefore..] : full;

            string? textAfter = null;
            try
            {
                var after = range.Clone();
                after.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, range, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
                after.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, text.DocumentRange, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
                textAfter = after.GetText(MaxAfter);
            }
            catch (Exception) { }

            return new FocusContext(false, textBefore, string.IsNullOrEmpty(selected) ? null : selected, string.IsNullOrWhiteSpace(textAfter) ? null : textAfter);
        }
        catch (Exception)
        {
            return null; // App unterstützt kein UIA, Zugriff verweigert, Element verschwunden …
        }
    }
}
