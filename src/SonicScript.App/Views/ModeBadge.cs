using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SonicScript.Core.Localization;
using SonicScript.Core.Settings;
using SonicScript.Platform.Windows;

namespace SonicScript.App.Views;

/// <summary>
/// Kleines, immer sichtbares Abzeichen mit dem aktuellen Modus. ‹ › wechseln den Modus, ziehen verschiebt es.
/// Nimmt nie den Fokus (Text landet weiter im Zielfenster).
/// </summary>
public sealed class ModeBadge : Window
{
    private readonly TextBlock _symbol = new() { Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _name = new() { Foreground = Brushes.White, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _pill;
    private bool _styled;

    public event Action? NextClicked;
    public event Action? PreviousClicked;
    public event Action<PixelPoint>? Moved;

    public ModeBadge()
    {
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Title = "SonicScript";

        var prev = ArrowButton("‹", Strings.Get("badge.prev"));
        var next = ArrowButton("›", Strings.Get("badge.next"));
        prev.Click += (_, _) => PreviousClicked?.Invoke();
        next.Click += (_, _) => NextClicked?.Invoke();

        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(4, 0), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeAll), Children = { _symbol, _name } };
        label.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };
        ToolTip.SetTip(label, Strings.Get("badge.tip"));

        _pill = new Border
        {
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(4, 3),
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { prev, label, next } },
        };
        Content = _pill;
        SetState("idle");

        Opened += (_, _) =>
        {
            if (!_styled && TryGetPlatformHandle()?.Handle is { } h) WindowStyles.MakeOverlay(h);
            _styled = true;
        };
        PositionChanged += (_, e) => Moved?.Invoke(e.Point);
    }

    private static Button ArrowButton(string text, string tip)
    {
        var b = new Button
        {
            Content = text,
            Padding = new Thickness(7, 0),
            MinHeight = 22,
            FontSize = 15,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(b, tip);
        return b;
    }

    public void Update(CycleEntry entry, string state)
    {
        _symbol.Text = entry.Symbol;
        _name.Text = entry.Name;
        SetState(state);
    }

    private void SetState(string state)
    {
        var (top, bottom) = ModeIcons.Colors(state);
        _pill.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1E, 0x1B, 0x2E));
        _pill.BorderBrush = new SolidColorBrush(state == "idle" ? Color.FromArgb(0x88, bottom.R, bottom.G, bottom.B) : top);
    }

    /// <summary>Gespeicherte Position oder unten rechts über der Taskleiste.</summary>
    public void Place(int? x, int? y)
    {
        var screen = Screens.Primary;
        if (screen is null) return;
        var area = screen.WorkingArea;
        if (x is { } px && y is { } py && Screens.All.Any(s => s.WorkingArea.Contains(new PixelPoint(px + 10, py + 10))))
        {
            Position = new PixelPoint(px, py);
            return;
        }
        var s2 = screen.Scaling;
        Position = new PixelPoint(area.X + area.Width - (int)(240 * s2), area.Y + area.Height - (int)(44 * s2));
    }
}
