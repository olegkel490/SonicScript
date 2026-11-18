using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SonicScript.Core.Settings;

namespace SonicScript.App.Views;

/// <summary>
/// Kurze Einblendung beim Moduswechsel (Variante C), wie die Lautstärke-Anzeige von Windows:
/// großes Symbol, Name, Tasten und Position in der Liste. Verschwindet nach 1,6 s, nimmt nie den Fokus.
/// </summary>
public sealed class ModeSwitchOsd : Window
{
    private readonly Border _root;
    private readonly TextBlock _symbol = new() { FontSize = 30, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, MinWidth = 40, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _name = new() { FontSize = 16, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _hint = new() { FontSize = 12 };
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(0, 6, 0, 0) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1600) };

    public ModeSwitchOsd()
    {
        Title = "SonicScript";
        _root = new Border
        {
            Padding = new Thickness(18, 12, 22, 12),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 14,
                Children =
                {
                    _symbol,
                    new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _name, _hint, _dots } },
                },
            },
        };
        Content = _root;
        OverlayChrome.Apply(this, _root);
        _timer.Tick += (_, _) => { _timer.Stop(); Hide(); };
        Opened += (_, _) => Place();
        SizeChanged += (_, _) => Place();
    }

    public void ShowEntry(CycleEntry entry, IReadOnlyList<CycleEntry> cycle, string nextKey, string prevKey)
    {
        var p = OverlayChrome.Palette;
        OverlayChrome.ApplyPalette(this, _root, p);
        _symbol.Text = entry.Symbol;
        _symbol.Foreground = new SolidColorBrush(p.Accent);
        _name.Text = entry.Name;
        _hint.Text = $"‹ {prevKey}     › {nextKey}";
        _hint.Foreground = p.SubText;

        _dots.Children.Clear();
        foreach (var e in cycle)
        {
            _dots.Children.Add(new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = e.Id == entry.Id ? new SolidColorBrush(p.Accent) : p.Border,
            });
        }

        if (!IsVisible) Show();
        Place();
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Unten mittig über der Taskleiste, wie die Windows-Einblendungen.</summary>
    private void Place()
    {
        var screen = Screens.Primary;
        if (screen is null) return;
        var area = screen.WorkingArea;
        var s = screen.Scaling;
        var w = (int)Math.Ceiling(Bounds.Width * s);
        var h = (int)Math.Ceiling(Bounds.Height * s);
        Position = new PixelPoint(area.X + (area.Width - w) / 2, area.Y + area.Height - h - (int)(48 * s));
    }
}
