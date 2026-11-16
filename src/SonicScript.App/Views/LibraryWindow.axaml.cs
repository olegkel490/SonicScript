using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using SonicScript.Core.Localization;
using SonicScript.Core.Notes;
using SonicScript.Core.Settings;
using SonicScript.Platform.Windows;

namespace SonicScript.App.Views;

/// <summary>
/// Notiz-Bibliothek: Tagesansicht, Tag/Monat blättern. Klick auf eine Notiz kopiert sie und markiert sie als verwendet (grün).
/// </summary>
public sealed partial class LibraryWindow : Window
{
    private static readonly IBrush UsedBrush = new SolidColorBrush(Color.Parse("#4022C55E"));
    private AppSettings _settings;
    private DateOnly _day = DateOnly.FromDateTime(DateTime.Now);

    public LibraryWindow() : this(new AppSettings()) { }

    public LibraryWindow(AppSettings settings)
    {
        _settings = settings;
        AvaloniaXamlLoader.Load(this);
        Button("PrevDay").Click += (_, _) => Go(_day.AddDays(-1));
        Button("NextDay").Click += (_, _) => Go(_day.AddDays(1));
        Button("PrevMonth").Click += (_, _) => Go(_day.AddMonths(-1));
        Button("NextMonth").Click += (_, _) => Go(_day.AddMonths(1));
        Button("DayLabel").Click += (_, _) => Go(DateOnly.FromDateTime(DateTime.Now));
        Button("DeleteUsed").Click += async (_, _) => await DeleteAsync(usedOnly: true);
        Button("DeleteAll").Click += async (_, _) => await DeleteAsync(usedOnly: false);
        Button("OpenFolder").Click += (_, _) => OpenNotesFolder();
        Opened += (_, _) => Dock();
        Refresh();
    }

    private Button Button(string name) => this.FindControl<Button>(name)!;

    private NoteStore? Store => string.IsNullOrWhiteSpace(_settings.Notes.Folder) ? null : new NoteStore(_settings.Notes.Folder);

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        Dock();
        Refresh();
    }

    private void Go(DateOnly day)
    {
        _day = day;
        Refresh();
    }

    private void Dock()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        var area = screen.WorkingArea;
        var s = screen.Scaling;
        var w = (int)(Width * s);
        Height = area.Height / s;
        Position = new PixelPoint(_settings.Notes.LibraryDock == DockSide.Left ? area.X : area.X + area.Width - w, area.Y);
    }

    public void Refresh()
    {
        var list = this.FindControl<StackPanel>("List")!;
        list.Children.Clear();
        var today = DateOnly.FromDateTime(DateTime.Now);
        Button("DayLabel").Content = _day == today
            ? $"{Strings.Get("library.today")} · {_day.ToString("ddd dd.MM.yyyy", CultureInfo.CurrentCulture)}"
            : _day.ToString("ddd dd.MM.yyyy", CultureInfo.CurrentCulture);

        var store = Store;
        if (store is null)
        {
            this.FindControl<TextBlock>("Summary")!.Text = Strings.Get("library.no_folder");
            return;
        }

        RenderMonthDays(store);
        var notes = store.Read(_day);
        var used = notes.Count(n => n.Used);
        this.FindControl<TextBlock>("Summary")!.Text = Strings.Format("library.summary", notes.Count, used, notes.Count - used);

        if (notes.Count == 0)
        {
            list.Children.Add(new TextBlock { Text = Strings.Get("library.empty"), Classes = { "hint" } });
            return;
        }

        foreach (var note in notes.Reverse())
        {
            var btn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = note.Used ? UsedBrush : null,
                Padding = new Thickness(10, 8),
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = note.Time.ToString("HH:mm", CultureInfo.InvariantCulture), Classes = { "hint" } },
                        new TextBlock { Text = note.Text, TextWrapping = TextWrapping.Wrap },
                    },
                },
            };
            btn.Click += async (_, _) =>
            {
                await ClipboardService.SetTextAsync(note.Text);
                if (!note.Used) store.MarkUsed(_day, note);
                Toast.Show(Strings.Get("toast.copied"));
                Refresh();
            };
            list.Children.Add(btn);
        }
    }

    private void RenderMonthDays(NoteStore store)
    {
        var panel = this.FindControl<WrapPanel>("MonthDays")!;
        panel.Children.Clear();
        foreach (var d in store.DaysWithNotes(_day.Year, _day.Month))
        {
            var b = new Button
            {
                Content = d.Day.ToString(CultureInfo.InvariantCulture),
                Padding = new Thickness(6, 2),
                Margin = new Thickness(0, 0, 4, 4),
                FontWeight = d == _day ? FontWeight.Bold : FontWeight.Normal,
            };
            b.Click += (_, _) => Go(d);
            panel.Children.Add(b);
        }
    }

    private async Task DeleteAsync(bool usedOnly)
    {
        if (Store is not { } store) return;
        var ok = await ConfirmDialog.AskAsync(this, Strings.Get(usedOnly ? "library.confirm_used" : "library.confirm_all"));
        if (!ok) return;
        if (usedOnly) store.DeleteUsed(_day);
        else store.DeleteAll(_day);
        Refresh();
    }

    private void OpenNotesFolder()
    {
        if (Store is not { } store) return;
        var path = Path.GetDirectoryName(store.PathFor(_day))!;
        if (!Directory.Exists(path)) path = store.RootFolder;
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
