using System.Globalization;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using SonicScript.App.Services;
using SonicScript.Core.Hotkeys;
using SonicScript.Core.LanguageModel;
using SonicScript.Core.Localization;
using SonicScript.Core.Modes;
using SonicScript.Core.Settings;
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
        RemoveModeCommand = new RelayCommand<ModeDefinition>(m => { _s.Modes.Remove(m); RefreshLists(); });
        RemoveRuleCommand = new RelayCommand<AppRule>(r => { _s.AppRules.Remove(r); RefreshLists(); });
        RemoveReplacementCommand = new RelayCommand<Replacement>(r => { _s.Dictionary.Replacements.Remove(r); RefreshLists(); });
        AvaloniaXamlLoader.Load(this);
        DataContext = _s;
        if (app is null) return; // Designer

        SetupGeneral();
        SetupDictation();
        SetupSpeech();
        SetupModes();
        SetupLists();

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
    }

    private void SetupDictation()
    {
        var mics = new List<(int Index, string Name)> { (-1, Strings.Get("settings.microphone.default")) };
        try { mics.AddRange(AudioRecorder.Microphones().Select(m => (m.Index, m.Name))); }
        catch (Exception ex) { Log.Error("Microphones", ex); }
        FillCombo(Get<ComboBox>("Microphone"), mics, m => m.Name, mics.FirstOrDefault(m => m.Index == _s.Transcription.MicrophoneDevice),
            m => _s.Transcription.MicrophoneDevice = m.Index);
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

        Get<Button>("AddMode").Click += (_, _) =>
        {
            _s.Modes.Add(new ModeDefinition { Kind = ModeKind.Custom, Name = Strings.Get("settings.modes.new_name"), Symbol = "✦" });
            RefreshLists();
        };
    }

    // ── Listen ─────────────────────────────────────────────────────────────────

    private void SetupLists()
    {
        Get<TextBox>("Terms").Text = string.Join(Environment.NewLine, _s.Dictionary.Terms);
        Get<Button>("AddRule").Click += (_, _) => { _s.AppRules.Add(new AppRule()); RefreshLists(); };
        Get<Button>("AddCurrentApp").Click += async (_, _) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            var fg = ForegroundWindow.Capture();
            if (!string.IsNullOrEmpty(fg.App.ProcessName) && !fg.App.ProcessName.Equals("SonicScript", StringComparison.OrdinalIgnoreCase))
            {
                _s.AppRules.Add(new AppRule { ProcessName = fg.App.ProcessName, ModeId = _s.DefaultModeId });
                RefreshLists();
            }
            Activate();
        };
        Get<Button>("AddReplacement").Click += (_, _) => { _s.Dictionary.Replacements.Add(new Replacement()); RefreshLists(); };
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
        RefreshLists();
        RenderHistory();
    }

    private void RefreshLists()
    {
        void Reset(string name, object source)
        {
            var c = Get<ItemsControl>(name);
            c.ItemsSource = null;
            c.ItemsSource = (System.Collections.IEnumerable)source;
        }
        Reset("ModesList", _s.Modes);
        Reset("RulesList", _s.AppRules);
        Reset("ReplacementsList", _s.Dictionary.Replacements);
    }

    private void RenderHistory()
    {
        var panel = Get<StackPanel>("HistoryList");
        panel.Children.Clear();
        foreach (var e in _app.History.GetAll())
        {
            var copy = new Button { Content = Strings.Get("settings.history.copy"), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
            var text = e.Final;
            copy.Click += async (_, _) => await ClipboardService.SetTextAsync(text);
            var row = new DockPanel();
            DockPanel.SetDock(copy, Dock.Right);
            row.Children.Add(copy);
            row.Children.Add(new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = $"{e.Timestamp.ToLocalTime():dd.MM. HH:mm} · {_s.FindMode(e.ModeId)?.Name ?? e.ModeId} · {e.App}", Classes = { "hint" } },
                    new TextBlock { Text = e.Final, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxHeight = 80 },
                },
            });
            panel.Children.Add(new Border { Child = row, Padding = new Avalonia.Thickness(8), BorderBrush = Avalonia.Media.Brushes.Gray, BorderThickness = new Avalonia.Thickness(0, 0, 0, 1) });
        }
    }

    // ── Speichern ──────────────────────────────────────────────────────────────

    private void Save()
    {
        var error = Get<TextBlock>("SaveError");
        var names = new Dictionary<string, string> { ["dictate"] = Strings.Get("settings.hotkey.dictate"), ["note"] = Strings.Get("settings.notes.hotkey"), ["library"] = Strings.Get("settings.notes.library_hotkey") };
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
