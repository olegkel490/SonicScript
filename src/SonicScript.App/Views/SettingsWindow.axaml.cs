using System.Globalization;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using SonicScript.App.Services;
using SonicScript.Core;
using SonicScript.Core.Hotkeys;
using SonicScript.Core.LanguageModel;
using SonicScript.Core.Localization;
using SonicScript.Core.Modes;
using SonicScript.Core.Settings;
using SonicScript.Core.Stats;
using SonicScript.Core.Transcription;
using SonicScript.LocalWhisper;
using SonicScript.Platform.Windows;

namespace SonicScript.App.Views;

public sealed partial class SettingsWindow : Window
{
    private readonly AppController _app;
    private readonly AppSettings _s;
    private CancellationTokenSource? _downloadCts;

    public SettingsWindow() : this(null!) { }

    public SettingsWindow(AppController app)
    {
        _app = app;
        _s = app?.CloneSettings() ?? new AppSettings();
        RemoveModeCommand = new RelayCommand<ModeDefinition>(m => _s.Modes.Remove(m));
        RemoveRuleCommand = new RelayCommand<AppRule>(r => _s.AppRules.Remove(r));
        RemoveReplacementCommand = new RelayCommand<Replacement>(r => _s.Dictionary.Replacements.Remove(r));
        AvaloniaXamlLoader.Load(this);
        DataContext = _s;
        if (app is null) return; // Designer

        SetupGeneral();
        SetupDictation();
        SetupSpeech();
        SetupModes();
        SetupLists();
        SetupStats();
        SetupDiagnose();
        RenderReminders();

        Get<Button>("SaveButton").Click += (_, _) => Save();
        Get<Button>("CancelButton").Click += (_, _) => Close();
        Closed += (_, _) => _downloadCts?.Cancel();
    }

    public ICommand RemoveModeCommand { get; }
    public ICommand RemoveRuleCommand { get; }
    public ICommand RemoveReplacementCommand { get; }

    private T Get<T>(string name) where T : Control => this.FindControl<T>(name)!;

    public void SelectTab(string? tab)
    {
        if (tab is null) return;
        var name = "Tab" + char.ToUpperInvariant(tab[0]) + tab[1..];
        if (this.FindControl<TabItem>(name) is { } item) Get<TabControl>("Tabs").SelectedItem = item;
    }

    // ── Allgemein ──────────────────────────────────────────────────────────────

    private void SetupGeneral()
    {
        FillCombo(Get<ComboBox>("UiLanguage"), Choices.UiLanguages, v => Strings.Get("enum.UiLanguage." + v), _s.UiLanguage, v => _s.UiLanguage = v);
        Get<TextBlock>("About").Text = Strings.Format("settings.about", AppController.CurrentVersion.ToString(3));
        Get<Button>("ExportSettings").Click += async (_, _) => await ExportSettingsAsync();
        Get<Button>("ImportSettings").Click += async (_, _) => await ImportSettingsAsync();
    }

    private static readonly FilePickerFileType JsonFiles = new("SonicScript (*.json)") { Patterns = ["*.json"] };

    private async Task ExportSettingsAsync()
    {
        var status = Get<TextBlock>("TransferStatus");
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = $"SonicScript-Einstellungen-{DateTime.Now:yyyy-MM-dd}.json",
                DefaultExtension = "json",
                FileTypeChoices = [JsonFiles],
            });
            if (file?.TryGetLocalPath() is not { } path) return;
            // Gespeicherter Stand (nicht ungespeicherte Änderungen in diesem Fenster).
            await File.WriteAllTextAsync(path, SettingsTransfer.Export(_app.CloneSettings(), AppController.CurrentVersion.ToString(3)));
            status.Text = Strings.Format("settings.transfer.exported", path);
        }
        catch (Exception ex)
        {
            Log.Error("Export settings", ex);
            status.Text = Strings.Format("settings.transfer.failed", ex.Message);
        }
    }

    private async Task ImportSettingsAsync()
    {
        var status = Get<TextBlock>("TransferStatus");
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false, FileTypeFilter = [JsonFiles] });
            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
            var imported = SettingsTransfer.Import(await File.ReadAllTextAsync(path), _app.CloneSettings());
            _app.ApplySettings(imported);
            Toast.Show(Strings.Get("settings.transfer.imported"));
            Close();
        }
        catch (FormatException)
        {
            status.Text = Strings.Get("settings.transfer.invalid");
        }
        catch (Exception ex)
        {
            Log.Error("Import settings", ex);
            status.Text = Strings.Format("settings.transfer.failed", ex.Message);
        }
    }

    private void SetupDictation()
    {
        var mics = new List<(int Index, string Name)> { (-1, Strings.Get("settings.microphone.default")) };
        try { mics.AddRange(AudioRecorder.Microphones().Select(m => (m.Index, m.Name))); }
        catch (Exception ex) { Log.Error("Microphones", ex); }
        var name = _s.Transcription.MicrophoneName;
        var current = !string.IsNullOrWhiteSpace(name)
            ? mics.FirstOrDefault(m => m.Index >= 0 && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
            : mics.FirstOrDefault(m => m.Index == _s.Transcription.MicrophoneDevice);
        FillCombo(Get<ComboBox>("Microphone"), mics, m => m.Name, current == default ? mics[0] : current, m =>
        {
            _s.Transcription.MicrophoneDevice = m.Index;
            _s.Transcription.MicrophoneName = m.Index < 0 ? "" : m.Name;
        });
    }

    // ── Spracherkennung ────────────────────────────────────────────────────────

    private void SetupSpeech()
    {
        FillCombo(Get<ComboBox>("SpeechLanguage"), Choices.SpeechLanguages,
            v => v == "auto" ? Strings.Get("settings.speech.language.auto") : CultureInfo.GetCultureInfo(v).NativeName,
            _s.Transcription.Language, v => _s.Transcription.Language = v);

        var models = WhisperModelCatalog.All.ToList();
        FillCombo(Get<ComboBox>("LocalModel"), models, m => $"{m.Id} – {Strings.Get("model." + m.Id)}",
            WhisperModelCatalog.Find(_s.Transcription.LocalModelId) ?? WhisperModelCatalog.Default,
            m => { _s.Transcription.LocalModelId = m.Id; UpdateModelStatus(); });
        UpdateModelStatus();

        Get<Button>("DownloadModel").Click += async (_, _) => await DownloadSelectedModelAsync();
        Get<Button>("Benchmark").Click += async (_, _) => await BenchmarkAsync();

        Get<ComboBox>("CloudPreset").SelectionChanged += (_, _) =>
        {
            var url = ProviderPresets.BaseUrl(_s.Transcription.Cloud.Preset);
            if (url.Length > 0) Get<TextBox>("CloudBaseUrl").Text = url;
        };
        Get<TextBox>("CloudKey").Watermark = _app.Secrets.Has(AppController.TranscriptionSecret)
            ? "•••••••• (" + Strings.Get("common.apikey.placeholder") + ")" : "";
    }

    private WhisperModelInfo SelectedModel => WhisperModelCatalog.Find(_s.Transcription.LocalModelId) ?? WhisperModelCatalog.Default;

    private void UpdateModelStatus()
    {
        var m = SelectedModel;
        var installed = _app.ModelDownloader.IsInstalled(m);
        Get<TextBlock>("ModelStatus").Text = installed
            ? "✓ " + Strings.Get("settings.speech.installed")
            : Strings.Format("settings.speech.not_installed", m.ApproxMegabytes);
        Get<Button>("DownloadModel").IsEnabled = !installed;
        Get<Button>("Benchmark").IsEnabled = installed;
    }

    private async Task DownloadSelectedModelAsync()
    {
        var model = SelectedModel;
        var bar = Get<ProgressBar>("DownloadProgress");
        var button = Get<Button>("DownloadModel");
        _downloadCts = new CancellationTokenSource();
        button.IsEnabled = false;
        bar.IsVisible = true;
        try
        {
            await _app.ModelDownloader.DownloadAsync(model, new Progress<double>(p => bar.Value = p), _downloadCts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error("Download", ex);
            Get<TextBlock>("BenchmarkResult").Text = Strings.Format("setup.error", ex.Message);
        }
        finally
        {
            bar.IsVisible = false;
            UpdateModelStatus();
        }
    }

    private async Task BenchmarkAsync()
    {
        var model = SelectedModel;
        var result = Get<TextBlock>("BenchmarkResult");
        var button = Get<Button>("Benchmark");
        button.IsEnabled = false;
        result.Text = Strings.Get("setup.benchmarking");
        try
        {
            using var t = new LocalWhisperTranscriber(_app.ModelDownloader.PathFor(model), _s.Transcription.UseGpu);
            var rtf = await t.BenchmarkAsync(AppPaths.Recordings, CancellationToken.None);
            var recommendation = ModelRecommender.RecommendAfterBenchmark(model.Id, rtf);
            result.Text = Strings.Format("settings.speech.benchmark_result", rtf * 10,
                LocalWhisperTranscriber.LoadedRuntime ?? "?", recommendation);
        }
        catch (Exception ex)
        {
            Log.Error("Benchmark", ex);
            result.Text = Strings.Format("setup.error", ex.Message);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    // ── KI-Modi ────────────────────────────────────────────────────────────────

    private void SetupModes()
    {
        Get<ComboBox>("LmPreset").SelectionChanged += (_, _) =>
        {
            var url = ProviderPresets.BaseUrl(_s.LanguageModel.Endpoint.Preset);
            if (url.Length > 0) Get<TextBox>("LmBaseUrl").Text = url;
        };
        Get<TextBox>("LmKey").Watermark = _app.Secrets.Has(AppController.LanguageModelSecret)
            ? "•••••••• (" + Strings.Get("common.apikey.placeholder") + ")" : "";

        var list = Get<ComboBox>("LmModelList");
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is string id) Get<TextBox>("LmModel").Text = id;
        };

        Get<Button>("LmFetch").Click += async (_, _) =>
        {
            var status = Get<TextBlock>("LmStatus");
            try
            {
                var key = Get<TextBox>("LmKey").Text;
                if (string.IsNullOrEmpty(key)) key = _app.Secrets.Load(AppController.LanguageModelSecret);
                var models = await OpenAiCompatibleChatClient.ListModelsAsync(_app.Http, _s.LanguageModel.Endpoint.BaseUrl, key, CancellationToken.None);
                list.ItemsSource = models;
                list.IsVisible = models.Count > 0;
                status.Text = Strings.Format("settings.modes.test_ok", models.Count);
            }
            catch (Exception ex)
            {
                status.Text = Strings.Format("settings.modes.test_failed", ex.Message);
            }
        };

        var templates = new MenuFlyout();
        var german = Strings.Language == "de";
        foreach (var t in ModeTemplates.All)
        {
            var template = t;
            var item = new MenuItem { Header = $"{t.Symbol}  {(german ? t.NameDe : t.NameEn)}" };
            item.Click += (_, _) => _s.Modes.Add(template.Create(german));
            templates.Items.Add(item);
        }
        Get<Button>("AddTemplate").Flyout = templates;

        Get<Button>("AddMode").Click += (_, _) =>
        {
            _s.Modes.Add(new ModeDefinition { Kind = ModeKind.Custom, Name = Strings.Get("settings.modes.new_name"), Symbol = "✦" });
        };
    }

    // ── Listen ─────────────────────────────────────────────────────────────────

    private void SetupLists()
    {
        Get<TextBox>("Terms").Text = string.Join(Environment.NewLine, _s.Dictionary.Terms);
        Get<Button>("AddRule").Click += (_, _) => _s.AppRules.Add(new AppRule());
        Get<Button>("AddCurrentApp").Click += async (_, _) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            var fg = ForegroundWindow.Capture();
            if (!string.IsNullOrEmpty(fg.App.ProcessName) && !fg.App.ProcessName.Equals("SonicScript", StringComparison.OrdinalIgnoreCase))
            {
                _s.AppRules.Add(new AppRule { ProcessName = fg.App.ProcessName, ModeId = _s.DefaultModeId });
            }
            Activate();
        };
        Get<Button>("AddReplacement").Click += (_, _) => _s.Dictionary.Replacements.Add(new Replacement());
        Get<Button>("BrowseNotes").Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            {
                _s.Notes.Folder = path;
                DataContext = null;
                DataContext = _s;
            }
        };
        Get<Button>("ClearHistory").Click += (_, _) => { _app.History.Clear(); RenderHistory(); };
        Get<TextBox>("HistorySearch").TextChanged += (_, _) => RenderHistory();
        BindLists();
        RenderHistory();
    }

    /// <summary>Die Listen sind ObservableCollections – Hinzufügen/Entfernen aktualisiert die Ansicht selbst.</summary>
    private void BindLists()
    {
        Get<ItemsControl>("ModesList").ItemsSource = _s.Modes;
        Get<ItemsControl>("RulesList").ItemsSource = _s.AppRules;
        Get<ItemsControl>("ReplacementsList").ItemsSource = _s.Dictionary.Replacements;
    }

    private void RenderHistory()
    {
        var panel = Get<StackPanel>("HistoryList");
        panel.Children.Clear();
        var query = Get<TextBox>("HistorySearch").Text?.Trim() ?? "";
        var entries = _app.History.GetAll()
            .Where(e => query.Length == 0 ||
                        e.Final.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                        e.Verbatim.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                        (e.App ?? "").Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Take(200);

        foreach (var e in entries)
        {
            var buttons = new StackPanel { Spacing = 4, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
            var copy = new Button { Content = Strings.Get("settings.history.copy"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            var final = e.Final;
            copy.Click += async (_, _) => { await ClipboardService.SetTextAsync(final); Toast.Show(Strings.Get("toast.copied")); };
            buttons.Children.Add(copy);
            var reprocess = new Button { Content = Strings.Get("settings.history.reprocess"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            var menu = new MenuFlyout();
            var entry = e;
            foreach (var m in _s.Modes)
            {
                var mode = m;
                var item = new MenuItem { Header = $"{m.Symbol}  {m.Name}" };
                item.Click += async (_, _) => await _app.ReprocessAsync(entry, mode);
                menu.Items.Add(item);
            }
            reprocess.Flyout = menu;
            buttons.Children.Add(reprocess);
            if (e.Verbatim != e.Final && e.Verbatim.Length > 0)
            {
                var verbatim = new Button { Content = Strings.Get("settings.history.copy_verbatim"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
                var v = e.Verbatim;
                verbatim.Click += async (_, _) => { await ClipboardService.SetTextAsync(v); Toast.Show(Strings.Get("toast.copied")); };
                buttons.Children.Add(verbatim);
            }

            var row = new DockPanel();
            DockPanel.SetDock(buttons, Dock.Right);
            row.Children.Add(buttons);
            row.Children.Add(new StackPanel
            {
                Margin = new Avalonia.Thickness(0, 0, 8, 0),
                Children =
                {
                    new TextBlock { Text = $"{e.Timestamp.ToLocalTime():dd.MM. HH:mm} · {_s.FindMode(e.ModeId)?.Name ?? e.ModeId} · {e.App}", Classes = { "hint" } },
                    new TextBlock { Text = e.Final, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxHeight = 100 },
                },
            });
            panel.Children.Add(new Border { Child = row, Padding = new Avalonia.Thickness(8), BorderBrush = Avalonia.Media.Brushes.Gray, BorderThickness = new Avalonia.Thickness(0, 0, 0, 1) });
        }
    }

    // ── Statistik & Kosten ─────────────────────────────────────────────────────

    private void SetupStats()
    {
        RenderStats();
        Get<Button>("ClearStats").Click += (_, _) => { _app.Stats.Clear(); RenderStats(); };
        Get<Button>("RefreshCost").Click += async (_, _) => await LoadCostAsync();
        var loaded = false;
        Get<TabControl>("Tabs").SelectionChanged += async (_, _) =>
        {
            if (loaded || Get<TabControl>("Tabs").SelectedItem != this.FindControl<TabItem>("TabStats")) return;
            loaded = true;
            RenderStats();
            await LoadCostAsync();
        };
    }

    private void RenderStats()
    {
        var grid = Get<Grid>("StatsGrid");
        grid.Children.Clear();
        grid.RowDefinitions.Clear();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var columns = new (string Key, StatsSummary S)[]
        {
            ("settings.stats.today", _app.Stats.Summary(today, today)),
            ("settings.stats.week", _app.Stats.Summary(monday, today)),
            ("settings.stats.month", _app.Stats.Summary(today.AddDays(-29), today)),
            ("settings.stats.total", _app.Stats.Summary(null, today)),
        };
        string Dur(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min" : $"{t.Seconds} s";
        var rows = new (string Key, Func<StatsSummary, string> Value)[]
        {
            ("", _ => ""),
            ("settings.stats.dictations", x => x.Dictations.ToString("N0", CultureInfo.CurrentCulture)),
            ("settings.stats.words", x => x.Words.ToString("N0", CultureInfo.CurrentCulture)),
            ("settings.stats.spoken", x => Dur(x.Spoken)),
            ("settings.stats.saved", x => Dur(x.Saved)),
        };
        for (var r = 0; r < rows.Length; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = r == 0 ? "" : Strings.Get(rows[r].Key), Margin = new Avalonia.Thickness(0, 2, 16, 2) };
            Grid.SetRow(label, r);
            grid.Children.Add(label);
            for (var c = 0; c < columns.Length; c++)
            {
                var cell = new TextBlock
                {
                    Text = r == 0 ? Strings.Get(columns[c].Key) : rows[r].Value(columns[c].S),
                    FontWeight = r == 0 || r == rows.Length - 1 ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal,
                    Margin = new Avalonia.Thickness(0, 2, 16, 2),
                };
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c + 1);
                grid.Children.Add(cell);
            }
        }
        var top = columns[2].S.TopModes;
        Get<TextBlock>("StatsModes").Text = top.Count == 0 ? "" :
            Strings.Get("settings.stats.top_modes") + " " + string.Join(", ", top.Select(t => $"{_s.FindMode(t.ModeId)?.Name ?? t.ModeId} ({t.Count})"));
    }

    private async Task LoadCostAsync()
    {
        var info = Get<TextBlock>("CostInfo");
        info.Text = Strings.Get("settings.cost.loading");
        try
        {
            var u = await _app.GetOpenRouterUsageAsync(CancellationToken.None);
            if (u is null) { info.Text = Strings.Get("settings.cost.none"); return; }
            string Usd(double? v) => v is null ? "–" : "$" + v.Value.ToString("0.00", CultureInfo.InvariantCulture);
            var text = Strings.Format("settings.cost.values", Usd(u.Day), Usd(u.Week), Usd(u.Month), Usd(u.Total));
            if (u.Remaining is not null) text += " · " + Strings.Format("settings.cost.remaining", Usd(u.Remaining));
            info.Text = text;
        }
        catch (Exception ex)
        {
            Log.Error("OpenRouter usage", ex);
            info.Text = Strings.Format("settings.transfer.failed", ex.Message);
        }
    }

    private void RenderReminders()
    {
        var panel = Get<StackPanel>("ReminderList");
        panel.Children.Clear();
        var pending = _app.Reminders.Pending();
        if (pending.Count == 0) return;
        panel.Children.Add(new TextBlock { Text = Strings.Get("settings.notes.reminders_pending"), FontWeight = Avalonia.Media.FontWeight.SemiBold });
        foreach (var r in pending)
        {
            var id = r.Id;
            var remove = new Button { Content = "✕", Padding = new Avalonia.Thickness(6, 0) };
            remove.Click += (_, _) => { _app.Reminders.Complete(id); RenderReminders(); };
            panel.Children.Add(new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    remove,
                    new TextBlock { Text = r.Due.ToString("ddd dd.MM. HH:mm", CultureInfo.CurrentCulture), Width = 120, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                    new TextBlock { Text = r.Text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 440, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                },
            });
        }
    }

    // ── Diagnose ───────────────────────────────────────────────────────────────

    private void SetupDiagnose()
    {
        Get<Button>("RunDiagnose").Click += async (_, _) => await RunDiagnoseAsync();
        Get<Button>("SaveReport").Click += async (_, _) => await SaveReportAsync();
        var location = AppLocation.Current;
        Get<TextBlock>("LocationInfo").Text = location.IsSynced
            ? Strings.Format("settings.location.synced", location.ExePath, AppLocation.RecommendedPath)
            : Strings.Format("settings.location.ok", location.ExePath);
        var move = Get<Button>("MoveApp");
        move.IsVisible = location.CanMove;
        move.Click += (_, _) => _app.MoveToRecommendedLocation();
    }

    private IReadOnlyList<DiagnosticResult> _lastDiagnostics = [];

    private async Task RunDiagnoseAsync()
    {
        var panel = Get<StackPanel>("DiagResults");
        var button = Get<Button>("RunDiagnose");
        button.IsEnabled = false;
        panel.Children.Clear();
        panel.Children.Add(new TextBlock { Text = Strings.Get("settings.diag.running"), Classes = { "hint" } });
        try
        {
            _lastDiagnostics = await _app.RunDiagnosticsAsync();
            panel.Children.Clear();
            foreach (var r in _lastDiagnostics)
            {
                var color = r.Status switch
                {
                    DiagnosticStatus.Ok => "#22A06B",
                    DiagnosticStatus.Warning => "#D9A400",
                    DiagnosticStatus.Error => "#E5484D",
                    _ => "#888888",
                };
                var symbol = r.Status switch { DiagnosticStatus.Ok => "✓", DiagnosticStatus.Warning => "!", DiagnosticStatus.Error => "✕", _ => "–" };
                panel.Children.Add(new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = symbol, Width = 16, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = Avalonia.Media.Brush.Parse(color) },
                        new TextBlock { Text = r.Name, Width = 200, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                        new TextBlock { Text = r.Detail, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 420 },
                    },
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error("Diagnostics", ex);
            panel.Children.Clear();
            panel.Children.Add(new TextBlock { Text = ex.Message });
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async Task SaveReportAsync()
    {
        var status = Get<TextBlock>("ReportStatus");
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = $"SonicScript-Fehlerbericht-{DateTime.Now:yyyy-MM-dd-HHmm}.zip",
                DefaultExtension = "zip",
                FileTypeChoices = [new FilePickerFileType("ZIP") { Patterns = ["*.zip"] }],
            });
            if (file?.TryGetLocalPath() is not { } path) return;
            await _app.WriteSupportReportAsync(path, _lastDiagnostics);
            status.Text = Strings.Format("settings.transfer.exported", path);
        }
        catch (Exception ex)
        {
            Log.Error("Support report", ex);
            status.Text = Strings.Format("settings.transfer.failed", ex.Message);
        }
    }

    // ── Speichern ──────────────────────────────────────────────────────────────

    private void Save()
    {
        var error = Get<TextBlock>("SaveError");
        var names = new Dictionary<string, string> { ["dictate"] = Strings.Get("settings.hotkey.dictate"), ["note"] = Strings.Get("settings.notes.hotkey"), ["library"] = Strings.Get("settings.notes.library_hotkey"), ["edit"] = Strings.Get("settings.hotkey.edit"), ["next"] = Strings.Get("settings.hotkey.next"), ["prev"] = Strings.Get("settings.hotkey.prev"), ["undo"] = Strings.Get("settings.hotkey.undo"), ["continue"] = Strings.Get("settings.hotkey.continue"), ["mic"] = Strings.Get("settings.hotkey.microphone") };
        foreach (var m in _s.Modes) names["mode:" + m.Id] = m.Name;
        var conflicts = HotkeyConflicts.Find(_s.BuildHotkeyBindings());
        var blocking = conflicts.Where(c => c.Kind != ConflictKind.ReservedBySystem).ToList();
        string Name(string id) => names.TryGetValue(id, out var n) ? n : id;
        string Describe(HotkeyConflict c) => c.Kind switch
        {
            ConflictKind.Duplicate => Strings.Format("settings.conflict.duplicate", Name(c.BindingId), Name(c.OtherBindingId!)),
            ConflictKind.ReservedBySystem => Strings.Format("settings.conflict.reserved", Name(c.BindingId)),
            _ => Strings.Format("settings.conflict.invalid", Name(c.BindingId)),
        };
        if (blocking.Count > 0)
        {
            error.Text = string.Join(" · ", blocking.Select(Describe));
            return;
        }

        _s.Dictionary.Terms = (Get<TextBox>("Terms").Text ?? "")
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
        _s.Dictionary.Replacements.RemoveAll(r => string.IsNullOrWhiteSpace(r.From));
        _s.AppRules.RemoveAll(r => string.IsNullOrWhiteSpace(r.ProcessName) && string.IsNullOrWhiteSpace(r.TitleContains));

        var cloudKey = Get<TextBox>("CloudKey").Text;
        if (!string.IsNullOrEmpty(cloudKey)) _app.Secrets.Save(AppController.TranscriptionSecret, cloudKey.Trim());
        var lmKey = Get<TextBox>("LmKey").Text;
        if (!string.IsNullOrEmpty(lmKey)) _app.Secrets.Save(AppController.LanguageModelSecret, lmKey.Trim());

        _app.ApplySettings(_s);
        var reserved = conflicts.Where(c => c.Kind == ConflictKind.ReservedBySystem).ToList();
        if (reserved.Count > 0) Toast.Show(string.Join(" · ", reserved.Select(Describe)), isError: true);
        Close();
    }

    private static void FillCombo<T>(ComboBox combo, IReadOnlyList<T> values, Func<T, string> text, T current, Action<T> set)
    {
        combo.ItemsSource = values.Select(v => new ComboBoxItem { Content = text(v), Tag = v }).ToList();
        var idx = values.ToList().IndexOf(current);
        combo.SelectedIndex = Math.Max(0, idx);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: T v }) set(v);
        };
    }
}
