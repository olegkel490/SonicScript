using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SonicScript.Core.Localization;
using SonicScript.Core.Settings;
using SonicScript.Platform.Windows;

namespace SonicScript.App.Views;

/// <summary>
/// Modus-Abzeichen in der Taskleiste, links neben dem Infobereich (Variante B).
/// Linksklick = nächster Modus, Rechtsklick = vorheriger, Mausrad = blättern.
/// Ohne sichtbare Taskleiste (automatisch ausblenden) sitzt es über dem Infobereich; bei Vollbild-Apps verschwindet es.
/// Nimmt nie den Fokus.
/// </summary>
public sealed class ModeBadge : Window
{
    private const double Inset = 6;      // Abstand zum Taskleistenrand (DIP)
    private const double TrayGap = 6;    // Abstand zum Infobereich (DIP)

    private readonly Border _root;
    private readonly Ellipse _dot = new() { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _symbol = new() { FontWeight = FontWeight.Bold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _name = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private OverlayPalette _palette;
    private string _state = "idle";
    private CycleEntry? _entry;
    private bool _hiddenForFullscreen;

    public event Action? NextClicked;
    public event Action? PreviousClicked;

    public ModeBadge()
    {
        Title = "SonicScript";
        _root = new Border
        {
            Padding = new Thickness(10, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _dot, _symbol, _name },
            },
        };
        Content = _root;
        _palette = OverlayChrome.Palette;
        OverlayChrome.Apply(this, _root, small: true, sizeToContent: false);
        SizeToContent = SizeToContent.Width;
        Height = 36;
        ToolTip.SetTip(_root, Strings.Get("badge.tip"));

        _root.PointerPressed += (_, e) =>
        {
            var p = e.GetCurrentPoint(this).Properties;
            if (p.IsLeftButtonPressed) NextClicked?.Invoke();
            else if (p.IsRightButtonPressed) PreviousClicked?.Invoke();
        };
        _root.PointerWheelChanged += (_, e) =>
        {
            if (e.Delta.Y > 0) PreviousClicked?.Invoke();
            else if (e.Delta.Y < 0) NextClicked?.Invoke();
        };

        _timer.Tick += (_, _) => Relayout();
        Opened += (_, _) => { Relayout(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
        SizeChanged += (_, _) => Relayout();
    }

    public void Update(CycleEntry entry, string state)
    {
        _entry = entry;
        _state = state;
        Render();
        Relayout();
    }

    private void Render()
    {
        if (_entry is null) return;
        var color = OverlayChrome.StateColor(_state, _palette.Accent);
        _symbol.Text = _entry.Symbol;
        _symbol.Foreground = new SolidColorBrush(color);
        _dot.Fill = new SolidColorBrush(color);
        _dot.IsVisible = _state != "idle"; // Punkt nur bei Aufnahme/Verarbeitung
        _name.Text = OverlayChrome.ShortName(_entry.Name);
        ToolTip.SetTip(_root, $"{_entry.Symbol} {_entry.Name}\n{Strings.Get("badge.tip")}");
    }

    /// <summary>An die Taskleiste andocken (alle 1,5 s und bei Größenänderung – Taskleiste kann sich bewegen).</summary>
    private void Relayout()
    {
        // Läuft per Timer: Fehler (z. B. Taskleiste gerade nicht erreichbar) dürfen die App nie beenden.
        try
        {
            if (OperatingSystem.IsWindows()) RelayoutCore();
        }
        catch (Exception ex)
        {
            Log.Error("Mode badge layout", ex);
        }
    }

    private void RelayoutCore()
    {
        if (TryGetPlatformHandle()?.Handle is not { } hwnd) return;

        if (Taskbar.IsForegroundFullscreen())
        {
            if (IsVisible) { Hide(); _hiddenForFullscreen = true; }
            return;
        }
        if (_hiddenForFullscreen)
        {
            _hiddenForFullscreen = false;
            Show();
        }

        var tb = Taskbar.Get();
        var scale = RenderScaling;
        var palette = OverlayChrome.PaletteFor(tb is null ? OverlayChrome.IsDark : !tb.LightTheme);
        if (palette.Dark != _palette.Dark)
        {
            _palette = palette;
            OverlayChrome.ApplyPalette(this, _root, _palette, small: true);
            Render();
        }

        var w = (int)Math.Ceiling(Bounds.Width * scale);
        if (tb is null || tb.AutoHide || tb.Bounds.IsEmpty)
        {
            // Keine feste Taskleiste: über dem Infobereich unten rechts schweben.
            _name.IsVisible = true;
            Height = 34;
            var area = Screens.Primary?.WorkingArea ?? new Avalonia.PixelRect(0, 0, 1920, 1040);
            var h = (int)Math.Ceiling(Height * scale);
            Position = new PixelPoint(area.X + area.Width - w - (int)(12 * scale), area.Y + area.Height - h - (int)(12 * scale));
        }
        else if (tb.IsHorizontal)
        {
            _name.IsVisible = true;
            Height = Math.Max(24, tb.Bounds.Height / scale - 2 * Inset);
            var h = (int)Math.Ceiling(Height * scale);
            var right = (tb.NotifyArea?.Left ?? tb.Bounds.Right - (int)(240 * scale)) - (int)(TrayGap * scale);
            Position = new PixelPoint(right - w, tb.Bounds.Top + (tb.Bounds.Height - h) / 2);
        }
        else
        {
            // Senkrechte Taskleiste: nur das Symbol, über dem Infobereich.
            _name.IsVisible = false;
            Height = 32;
            var h = (int)Math.Ceiling(Height * scale);
            var bottom = (tb.NotifyArea?.Top ?? tb.Bounds.Bottom - (int)(300 * scale)) - (int)(TrayGap * scale);
            Position = new PixelPoint(tb.Bounds.Left + (tb.Bounds.Width - w) / 2, bottom - h);
        }

        // Klick auf die Taskleiste schiebt sie nach vorn – Abzeichen wieder darüberlegen.
        Taskbar.BringToTop(hwnd);
    }
}
