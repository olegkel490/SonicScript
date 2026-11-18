using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using SonicScript.Platform.Windows;

namespace SonicScript.App.Views;

/// <summary>Kurze Meldung unten rechts, ohne Fokus zu stehlen. Immer nur eine gleichzeitig.</summary>
public sealed partial class Toast : Window
{
    private static Toast? _current;
    private readonly DispatcherTimer _timer;

    public Toast()
    {
        AvaloniaXamlLoader.Load(this);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _timer.Tick += (_, _) => { _timer.Stop(); Close(); };
        PointerPressed += (_, _) => Close();
        OverlayChrome.Apply(this, this.FindControl<Border>("Box")!);
        Opened += (_, _) => PlaceBottomRight();
        Closed += (_, _) => { if (_current == this) _current = null; };
    }

    public static void Show(string message, bool isError = false) => Show(message, isError, null, null, null, null);

    /// <summary>Meldung mit bis zu zwei Knöpfen (z. B. "Übernehmen" / "Ignorieren"); bleibt länger stehen.</summary>
    public static void Show(string message, bool isError, string? primaryText, Action? primary, string? secondaryText, Action? secondary)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _current?.Close();
            var t = new Toast();
            t.FindControl<TextBlock>("Message")!.Text = message;
            if (primaryText is not null)
            {
                t.FindControl<StackPanel>("Actions")!.IsVisible = true;
                var p = t.FindControl<Button>("Primary")!;
                p.Content = primaryText;
                p.Click += (_, _) => { t.Close(); primary?.Invoke(); };
                var s = t.FindControl<Button>("Secondary")!;
                s.IsVisible = secondaryText is not null;
                s.Content = secondaryText;
                s.Click += (_, _) => { t.Close(); secondary?.Invoke(); };
            }
            if (isError)
            {
                var box = t.FindControl<Border>("Box")!;
                box.BorderBrush = new SolidColorBrush(Color.Parse("#E5484D"));
                box.BorderThickness = new Thickness(0, 0, 0, 3); // roter Streifen unten
            }
            t._timer.Interval = TimeSpan.FromSeconds(primaryText is not null ? 20 : isError ? 7 : 4);
            _current = t;
            t.Show();
            t._timer.Start();
        });
    }

    private void PlaceBottomRight()
    {
        var screen = Screens.Primary;
        if (screen is null) return;
        var area = screen.WorkingArea;
        var s = screen.Scaling;
        Position = new PixelPoint(
            area.X + area.Width - (int)(Bounds.Width * s) - (int)(16 * s),
            area.Y + area.Height - (int)(Bounds.Height * s) - (int)(60 * s)); // über dem Modus-Abzeichen
    }
}
