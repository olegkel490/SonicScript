using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SonicScript.Platform.Windows;

namespace SonicScript.App;

/// <summary>Farben der kleinen Overlays passend zum Windows-Design (hell/dunkel der Taskleiste, Akzentfarbe).</summary>
public sealed record OverlayPalette(IBrush Surface, IBrush Border, IBrush Text, IBrush SubText, Color Accent, bool Dark);

/// <summary>
/// Gemeinsames Aussehen für Taskleisten-Abzeichen, Moduswechsel-Einblendung, Aufnahme-Anzeige und Meldungen:
/// deckender Hintergrund im Windows-Design und echte abgerundete Fensterecken (Windows 11: DWM mit Schatten,
/// Windows 10: Fensterregion). Kein durchsichtiger Hintergrund nötig – also keine schwarzen Ecken bei Software-Rendering.
/// </summary>
public static class OverlayChrome
{
    public const double Radius = 8;      // Windows-11-Rundung
    public const double SmallRadius = 4; // Windows 11 "klein" (Taskleisten-Elemente)

    /// <summary>Overlays folgen dem Windows-Design von Taskleiste/Infobereich.</summary>
    public static bool IsDark => !Taskbar.SystemUsesLightTheme();

    public static OverlayPalette PaletteFor(bool dark)
    {
        var accent = dark ? Color.Parse("#4CC2FF") : Color.Parse("#005FB8");
        var res = dark ? "SystemAccentColorLight2" : "SystemAccentColorDark1";
        if (Application.Current?.TryGetResource(res, null, out var v) == true && v is Color c) accent = c;
        return dark
            ? new OverlayPalette(new SolidColorBrush(Color.Parse("#2C2C2C")), new SolidColorBrush(Color.Parse("#3D3D3D")),
                Brushes.White, new SolidColorBrush(Color.Parse("#C8C8C8")), accent, true)
            : new OverlayPalette(new SolidColorBrush(Color.Parse("#F9F9F9")), new SolidColorBrush(Color.Parse("#D6D6D6")),
                new SolidColorBrush(Color.Parse("#1B1B1B")), new SolidColorBrush(Color.Parse("#5C5C5C")), accent, false);
    }

    public static OverlayPalette Palette => PaletteFor(IsDark);

    /// <summary>Fenster als Overlay mit abgerundeten Ecken einrichten. <paramref name="root"/> ist das äußerste Element.</summary>
    public static void Apply(Window window, Border root, bool small = false, bool sizeToContent = true)
    {
        window.SystemDecorations = SystemDecorations.None;
        window.ShowInTaskbar = false;
        window.Topmost = true;
        window.ShowActivated = false;
        window.CanResize = false;
        if (sizeToContent) window.SizeToContent = SizeToContent.WidthAndHeight;
        window.TransparencyLevelHint = [WindowTransparencyLevel.None];
        ApplyPalette(window, root, Palette, small);

        window.Opened += (_, _) => Update(window, small);
        window.SizeChanged += (_, _) => Update(window, small);
    }

    public static void ApplyPalette(Window window, Border root, OverlayPalette p, bool small = false)
    {
        window.Background = p.Surface;
        window.Foreground = p.Text;
        root.Background = p.Surface;
        root.CornerRadius = new CornerRadius(small ? SmallRadius : Radius);
        // Unter Windows 11 zeichnet DWM Rahmen und Schatten; darunter selbst ein feiner Rahmen.
        root.BorderThickness = WindowStyles.HasNativeRoundedCorners ? new Thickness(0) : new Thickness(1);
        root.BorderBrush = p.Border;
    }

    private static void Update(Window window, bool small)
    {
        if (window.TryGetPlatformHandle()?.Handle is not { } h) return;
        WindowStyles.MakeOverlay(h);
        var scale = window.RenderScaling;
        WindowStyles.RoundCorners(h, (int)Math.Ceiling(window.Bounds.Width * scale), (int)Math.Ceiling(window.Bounds.Height * scale),
            (int)Math.Round((small ? SmallRadius : Radius) * scale), small);
    }

    public static Color StateColor(string state, Color accent) => state switch
    {
        "rec" => Color.Parse("#E5484D"),
        "busy" => Color.Parse("#F5A524"),
        _ => accent,
    };

    /// <summary>"SonicScript Mail" → "Mail"; "SonicScript+" und "SonicScript" bleiben.</summary>
    public static string ShortName(string name) =>
        name.StartsWith("SonicScript ", StringComparison.Ordinal) && name.Length > 12 ? name[12..] : name;
}
