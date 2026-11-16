using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;

namespace SonicScript.App.Views;

/// <summary>Sichtbare Fehlermeldung, wenn der Start scheitert (statt still zu beenden).</summary>
public static class StartupError
{
    public static void Show(Exception ex, IClassicDesktopStyleApplicationLifetime lifetime)
    {
        var close = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 90 };
        var window = new Window
        {
            Title = "SonicScript",
            Width = 640,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "SonicScript konnte nicht starten / could not start.", FontWeight = Avalonia.Media.FontWeight.SemiBold },
                    new TextBox { Text = ex.ToString(), IsReadOnly = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxHeight = 300 },
                    new TextBlock { Text = "Log: " + AppPaths.LogFile, Opacity = 0.7 },
                    close,
                },
            },
        };
        close.Click += (_, _) => window.Close();
        window.Closed += (_, _) => lifetime.Shutdown(1);
        window.Show();
    }
}
