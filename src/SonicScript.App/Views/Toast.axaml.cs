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
        Opened += (_, _) =>
        {
            if (TryGetPlatformHandle()?.Handle is { } h) WindowStyles.MakeOverlay(h);
            PlaceBottomRight();
        };
        Closed += (_, _) => { if (_current == this) _current = null; };
    }

    public static void Show(string message, bool isError = false)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _current?.Close();
            var t = new Toast();
            t.FindControl<TextBlock>("Message")!.Text = message;
            if (isError) t.FindControl<Border>("Box")!.BorderBrush = new SolidColorBrush(Color.Parse("#AAEF4444"));
            t._timer.Interval = TimeSpan.FromSeconds(isError ? 7 : 4);
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
            area.Y + area.Height - (int)(Bounds.Height * s) - (int)(16 * s));
    }
}
