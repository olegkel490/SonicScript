using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace SonicScript.App;

/// <summary>Zeichnet das Tray-Symbol mit dem Kürzel des aktuellen Modus (Farbe = Zustand).</summary>
public static class ModeIcons
{
    private static readonly Dictionary<string, WindowIcon> Cache = [];

    public static (Color Top, Color Bottom) Colors(string state) => state switch
    {
        "rec" => (Color.Parse("#B91C1C"), Color.Parse("#F87171")),
        "busy" => (Color.Parse("#B45309"), Color.Parse("#FBBF24")),
        _ => (Color.Parse("#0E7490"), Color.Parse("#38BDF8")),
    };

    public static WindowIcon Get(string symbol, string state)
    {
        var key = state + "|" + symbol;
        if (Cache.TryGetValue(key, out var icon)) return icon;
        icon = new WindowIcon(new MemoryStream(RenderPng(symbol, state)));
        Cache[key] = icon;
        return icon;
    }

    /// <summary>Symbol als PNG (64×64).</summary>
    public static byte[] RenderPng(string symbol, string state)
    {
        const int size = 64;
        var (top, bottom) = Colors(state);
        var text = string.IsNullOrWhiteSpace(symbol) ? "◉" : symbol.Trim();
        if (text.Length > 3) text = text[..3];
        var border = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(14),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(top, 0), new GradientStop(bottom, 1) },
            },
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontWeight = FontWeight.Bold,
                FontSize = text.Length switch { 1 => 40, 2 => 30, _ => 22 },
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        border.Measure(new Size(size, size));
        border.Arrange(new Rect(0, 0, size, size));
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        bitmap.Render(border);
        using var ms = new MemoryStream();
        bitmap.Save(ms);
        return ms.ToArray();
    }
}
