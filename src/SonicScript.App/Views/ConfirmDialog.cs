using Avalonia.Controls;
using Avalonia.Layout;
using SonicScript.Core.Localization;

namespace SonicScript.App.Views;

public static class ConfirmDialog
{
    public static Task<bool> AskAsync(Window owner, string question)
    {
        var dlg = new Window
        {
            Title = Strings.Get("confirm.title"),
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        var yes = new Button { Content = Strings.Get("common.yes"), IsDefault = true, MinWidth = 80 };
        var no = new Button { Content = Strings.Get("common.no"), IsCancel = true, MinWidth = 80 };
        yes.Click += (_, _) => dlg.Close(true);
        no.Click += (_, _) => dlg.Close(false);
        dlg.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = question, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 360 },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { no, yes } },
            },
        };
        return dlg.ShowDialog<bool>(owner);
    }
}
