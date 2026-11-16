using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using SonicScript.App.Services;
using SonicScript.Core.Localization;
using SonicScript.Core.Transcription;
using SonicScript.LocalWhisper;
using SonicScript.Platform.Windows;

namespace SonicScript.App.Views;

/// <summary>Erster Start: Modell nach Hardware empfehlen, laden, Geschwindigkeit messen, Notizordner wählen.</summary>
public sealed partial class SetupWindow : Window
{
    private readonly AppController _app;
    private readonly CancellationTokenSource _cts = new();
    private string _modelId;
    private string? _smaller;

    public SetupWindow() : this(null!) { }

    public SetupWindow(AppController app)
    {
        _app = app;
        AvaloniaXamlLoader.Load(this);
        var hw = new HardwareInfo(WindowStyles.HasVulkanRuntime, Environment.ProcessorCount);
        _modelId = ModelRecommender.RecommendInitial(hw);
        if (app is null) return;

        var models = WhisperModelCatalog.All.ToList();
        var combo = Get<ComboBox>("Model");
        combo.ItemsSource = models.Select(m => $"{m.Id} – {Strings.Get("model." + m.Id)} (~{m.ApproxMegabytes:0} MB)").ToList();
        combo.SelectedIndex = models.FindIndex(m => m.Id == _modelId);
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) _modelId = models[combo.SelectedIndex].Id; };

        Get<TextBlock>("Recommendation").Text = Strings.Format("setup.recommended", _modelId);
        Get<TextBox>("NotesFolder").Text = string.IsNullOrWhiteSpace(app.Settings.Notes.Folder) ? AppPaths.DefaultNotesFolder : app.Settings.Notes.Folder;
        Get<Button>("Finish").Content = Strings.Format("setup.finish", app.Settings.DictateHotkey);
        Get<Button>("Finish").IsEnabled = models.Any(app.ModelDownloader.IsInstalled);

        Get<Button>("Download").Click += async (_, _) => await DownloadAndBenchmarkAsync(_modelId);
        Get<Button>("UseSmaller").Click += async (_, _) =>
        {
            if (_smaller is null) return;
            _modelId = _smaller;
            combo.SelectedIndex = models.FindIndex(m => m.Id == _modelId);
            await DownloadAndBenchmarkAsync(_modelId);
        };
        Get<Button>("BrowseNotes").Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } p) Get<TextBox>("NotesFolder").Text = p;
        };
        Get<Button>("Finish").Click += (_, _) => FinishSetup();
        Closed += (_, _) => _cts.Cancel();
    }

    private T Get<T>(string name) where T : Control => this.FindControl<T>(name)!;

    private async Task DownloadAndBenchmarkAsync(string modelId)
    {
        var model = WhisperModelCatalog.Find(modelId)!;
        var status = Get<TextBlock>("Status");
        var progress = Get<ProgressBar>("Progress");
        Get<Button>("Download").IsEnabled = false;
        Get<Button>("UseSmaller").IsVisible = false;
        try
        {
            if (!_app.ModelDownloader.IsInstalled(model))
            {
                progress.IsVisible = true;
                await _app.ModelDownloader.DownloadAsync(model, new Progress<double>(p =>
                {
                    progress.Value = p;
                    status.Text = Strings.Format("setup.downloading", model.Id, p * 100);
                }), _cts.Token);
                progress.IsVisible = false;
            }

            status.Text = Strings.Get("setup.benchmarking");
            double rtf;
            using (var t = new LocalWhisperTranscriber(_app.ModelDownloader.PathFor(model), useGpu: true))
                rtf = await t.BenchmarkAsync(AppPaths.Recordings, _cts.Token);

            var recommended = ModelRecommender.RecommendAfterBenchmark(model.Id, rtf);
            if (recommended == model.Id)
            {
                status.Text = Strings.Format("setup.result_fast", rtf * 10);
            }
            else
            {
                _smaller = recommended;
                status.Text = Strings.Format("setup.result_slow", rtf * 10, recommended);
                Get<Button>("UseSmaller").IsVisible = true;
            }
            Get<Button>("Finish").IsEnabled = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error("Setup", ex);
            status.Text = Strings.Format("setup.error", ex.Message);
        }
        finally
        {
            progress.IsVisible = false;
            Get<Button>("Download").IsEnabled = true;
        }
    }

    private void FinishSetup()
    {
        var s = _app.CloneSettings();
        var installed = WhisperModelCatalog.Find(_modelId) is { } m && _app.ModelDownloader.IsInstalled(m)
            ? _modelId
            : WhisperModelCatalog.All.Where(_app.ModelDownloader.IsInstalled).OrderByDescending(x => x.QualityRank).First().Id;
        s.Transcription.LocalModelId = installed;
        s.Transcription.Engine = Core.Settings.TranscriptionEngine.Local;
        var folder = Get<TextBox>("NotesFolder").Text?.Trim();
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
            s.Notes.Folder = folder;
        }
        s.FirstRunCompleted = true;
        _app.ApplySettings(s);
        Toast.Show(Strings.Format("tray.tooltip", s.DictateHotkey));
        Close();
    }
}
