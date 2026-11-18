using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using SonicScript.Core.Localization;
using SonicScript.Core.Modes;
using SonicScript.Platform.Windows;

namespace SonicScript.App.Views;

/// <summary>Kleine Pille unten in der Bildschirmmitte. Nimmt nie den Fokus. Klick = fertig.</summary>
public sealed partial class RecordingIndicator : Window
{
    private static readonly double[] BarWeights = [0.5, 0.8, 1.0, 0.8, 0.5];
    private readonly Rectangle[] _bars;

    public event Action? Clicked;

    public RecordingIndicator()
    {
        AvaloniaXamlLoader.Load(this);
        _bars = this.FindControl<StackPanel>("Bars")!.Children.OfType<Rectangle>().ToArray();
        this.FindControl<Border>("Pill")!.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) Clicked?.Invoke();
        };
        ToolTip.SetTip(this.FindControl<Border>("Pill")!, Strings.Get("indicator.hint"));
        OverlayChrome.Apply(this, this.FindControl<Border>("Pill")!);
        var accent = new SolidColorBrush(OverlayChrome.Palette.Accent);
        foreach (var bar in _bars) bar.Fill = accent;
    }

    public void ShowRecording(ModeDefinition mode, bool isNote)
    {
        this.FindControl<TextBlock>("Symbol")!.Text = isNote ? "✎" : mode.Symbol;
        this.FindControl<TextBlock>("Label")!.Text = isNote ? Strings.Get("indicator.note") : mode.Name;
        this.FindControl<Ellipse>("Dot")!.Fill = new SolidColorBrush(Color.Parse("#EF4444"));
        this.FindControl<StackPanel>("Bars")!.IsVisible = true;
        SetPreview(null);
        ShowAtBottom();
    }

    public void ShowProcessing()
    {
        this.FindControl<TextBlock>("Label")!.Text = Strings.Get("indicator.processing");
        this.FindControl<Ellipse>("Dot")!.Fill = new SolidColorBrush(Color.Parse("#F59E0B"));
        this.FindControl<StackPanel>("Bars")!.IsVisible = false;
        ShowAtBottom();
    }

    /// <summary>Live-Vorschau des erkannten Texts (letzte ~160 Zeichen); null = ausblenden.</summary>
    public void SetPreview(string? text)
    {
        var preview = this.FindControl<TextBlock>("Preview")!;
        if (string.IsNullOrWhiteSpace(text))
        {
            if (!preview.IsVisible) return;
            preview.IsVisible = false;
        }
        else
        {
            var t = text.Trim();
            if (t.Length > 160) t = "…" + t[^160..].TrimStart();
            preview.Text = t;
            preview.IsVisible = true;
        }
        if (IsVisible) Dispatcher.UIThread.Post(ShowAtBottom, DispatcherPriority.Background); // Größe hat sich geändert
    }

    public void SetLevel(float level)
    {
        if (!IsVisible) return;
        for (var i = 0; i < _bars.Length; i++)
            _bars[i].Height = 4 + 16 * Math.Clamp(level * BarWeights[i], 0, 1);
    }

    public void HideIndicator()
    {
        if (IsVisible) Hide();
    }

    private void ShowAtBottom()
    {
        if (!IsVisible) Show();
        var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
        if (screen is null) return;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var w = (int)(Bounds.Width * scale);
        var h = (int)(Bounds.Height * scale);
        Position = new PixelPoint(area.X + (area.Width - w) / 2, area.Y + area.Height - h - (int)(24 * scale));
    }
}
