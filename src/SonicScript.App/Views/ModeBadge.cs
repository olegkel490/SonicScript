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
/// Modus-Feld in der Taskleiste, links neben dem Infobereich.
/// Normalfall: als Kindfenster in die Taskleiste eingebettet – gehört damit zur Taskleiste (kein Schatten, verschwindet
/// nicht beim Klick auf die Taskleiste, blendet sich mit ihr bei Vollbild aus).
/// Fallback (Einbetten nicht möglich oder Taskleiste automatisch ausgeblendet): schwebt über dem Infobereich.
/// Linksklick = nächster Modus, Rechtsklick = vorheriger, Mausrad = blättern. Nimmt nie den Fokus.
/// </summary>
public sealed class ModeBadge : Window
{
    private const double Inset = 6;      // Abstand zum Taskleistenrand (DIP)
    private const double TrayGap = 4;    // Abstand zum Infobereich (DIP)

    private readonly Border _root;
    private readonly Ellipse _dot = new() { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _symbol = new() { FontWeight = FontWeight.Bold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _name = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private OverlayPalette _palette;
    private string _state = "idle";
    private CycleEntry? _entry;
    private IntPtr _embeddedIn;
    private bool _embedFailed;
    private bool _hiddenForFullscreen;
    private (int X, int Y, int W, int H) _lastRect;

    public event Action? NextClicked;
    public event Action? PreviousClicked;

    /// <summary>Taskleiste neu gestartet oder Lage geändert (z. B. automatisch ausblenden) – Fenster neu anlegen.</summary>
    public event Action? RecreateRequested;

    public ModeBadge()
    {
        Title = "SonicScript";
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Topmost = false;
        TransparencyLevelHint = [WindowTransparencyLevel.None];
        SizeToContent = SizeToContent.Manual;
        Width = 120;
        Height = 36;

        _root = new Border
        {
            Padding = new Thickness(10, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _dot, _symbol, _name },
            },
        };
        Content = _root;
        _palette = OverlayChrome.Palette;
        ApplyLook();

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
    }

    public bool IsEmbedded => _embeddedIn != IntPtr.Zero;

    /// <summary>Wie ein Taskleisten-Knopf: dezente Fläche, kleine Rundung, kein eigener Rahmen.</summary>
    private void ApplyLook()
    {
        var surface = _palette.Dark ? Color.Parse("#2E2E2E") : Color.Parse("#E4E4E4");
        Background = new SolidColorBrush(surface);
        Foreground = _palette.Text;
        _root.Background = new SolidColorBrush(surface);
        _root.CornerRadius = new CornerRadius(OverlayChrome.SmallRadius);
        _root.BorderThickness = new Thickness(0);
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

    /// <summary>Breite für den aktuellen Inhalt in Pixeln.</summary>
    private int ContentWidthPx(double scale)
    {
        _root.Measure(Size.Infinity);
        return (int)Math.Ceiling(Math.Max(28, _root.DesiredSize.Width) * scale);
    }

    private void RelayoutCore()
    {
        if (TryGetPlatformHandle()?.Handle is not { } hwnd) return;
        var tb = Taskbar.Get();
        var scale = RenderScaling;

        var palette = OverlayChrome.PaletteFor(tb is null ? OverlayChrome.IsDark : !tb.LightTheme);
        if (palette.Dark != _palette.Dark)
        {
            _palette = palette;
            ApplyLook();
            Render();
        }

        var tray = Taskbar.TrayWindow;
        var canEmbed = !_embedFailed && tb is { AutoHide: false } && !tb.Bounds.IsEmpty && Taskbar.IsAlive(tray);

        if (IsEmbedded)
        {
            // Explorer neu gestartet oder Taskleiste jetzt automatisch ausgeblendet → neu anlegen.
            if (!canEmbed || tray != _embeddedIn)
            {
                RecreateRequested?.Invoke();
                return;
            }
        }
        else if (canEmbed)
        {
            if (Taskbar.Embed(hwnd, tray))
            {
                _embeddedIn = tray;
                _lastRect = default;
                Log.Info("Mode badge embedded into taskbar");
            }
            else
            {
                _embedFailed = true;
                Log.Info("Mode badge: embedding into taskbar failed, using floating badge");
            }
        }

        if (IsEmbedded) PlaceInTaskbar(hwnd, tb!, scale);
        else PlaceFloating(hwnd, tb, scale);
    }

    private void PlaceInTaskbar(IntPtr hwnd, TaskbarInfo tb, double scale)
    {
        int x, y, w, h;
        if (tb.IsHorizontal)
        {
            _name.IsVisible = true;
            h = tb.Bounds.Height - (int)Math.Round(2 * Inset * scale);
            w = ContentWidthPx(scale);
            var right = (tb.NotifyArea?.Left ?? tb.Bounds.Right - (int)(240 * scale)) - (int)(TrayGap * scale);
            x = right - w;
            y = tb.Bounds.Top + (tb.Bounds.Height - h) / 2;
        }
        else
        {
            _name.IsVisible = false; // senkrechte Taskleiste: nur das Symbol
            w = tb.Bounds.Width - (int)Math.Round(2 * Inset * scale);
            h = (int)Math.Round(32 * scale);
            var bottom = (tb.NotifyArea?.Top ?? tb.Bounds.Bottom - (int)(300 * scale)) - (int)(TrayGap * scale);
            x = tb.Bounds.Left + (tb.Bounds.Width - w) / 2;
            y = bottom - h;
        }

        if (_lastRect != (x, y, w, h))
        {
            _lastRect = (x, y, w, h);
            WindowStyles.RoundCorners(hwnd, w, h, (int)Math.Round(OverlayChrome.SmallRadius * scale), small: true, forceRegion: true);
        }
        // Immer wieder nach vorn: die Taskleiste zeichnet ihre eigenen Elemente sonst darüber.
        Taskbar.PlaceChild(hwnd, _embeddedIn, x, y, w, h);
    }

    private void PlaceFloating(IntPtr hwnd, TaskbarInfo? tb, double scale)
    {
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

        WindowStyles.MakeOverlay(hwnd);
        _name.IsVisible = true;
        var w = ContentWidthPx(scale);
        var h = (int)Math.Round(34 * scale);
        Width = w / scale;
        Height = h / scale;

        int x, y;
        if (tb is { AutoHide: false } && tb.IsHorizontal && !tb.Bounds.IsEmpty)
        {
            // In der Taskleiste, aber schwebend (Einbetten nicht möglich).
            h = tb.Bounds.Height - (int)Math.Round(2 * Inset * scale);
            Height = h / scale;
            var right = (tb.NotifyArea?.Left ?? tb.Bounds.Right - (int)(240 * scale)) - (int)(TrayGap * scale);
            x = right - w;
            y = tb.Bounds.Top + (tb.Bounds.Height - h) / 2;
        }
        else
        {
            var area = Screens.Primary?.WorkingArea ?? new Avalonia.PixelRect(0, 0, 1920, 1040);
            x = area.X + area.Width - w - (int)(12 * scale);
            y = area.Y + area.Height - h - (int)(12 * scale);
        }
        Position = new PixelPoint(x, y);
        WindowStyles.RoundCorners(hwnd, w, h, (int)Math.Round(OverlayChrome.SmallRadius * scale), small: true);
        Taskbar.BringToTop(hwnd);
    }
}
