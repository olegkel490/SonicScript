using System.Diagnostics;
using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using SonicScript.App.Views;
using SonicScript.Core.Audio;
using SonicScript.Core.Hotkeys;
using SonicScript.Core.LanguageModel;
using SonicScript.Core.Localization;
using SonicScript.Core.Modes;
using SonicScript.Core.Notes;
using SonicScript.Core.Pipeline;
using SonicScript.Core.Rules;
using SonicScript.Core.Settings;
using SonicScript.Core.Storage;
using SonicScript.Core.Transcription;
using SonicScript.Core.Updates;
using SonicScript.LocalWhisper;
using SonicScript.Platform.Windows;

namespace SonicScript.App.Services;

/// <summary>
/// Zentrale Steuerung: Tastenkürzel → Aufnahme → Pipeline → Einfügen/Notiz. Läuft auf dem UI-Thread.
/// </summary>
public sealed class AppController : IDisposable
{
    public const string TranscriptionSecret = "transcription";
    public const string LanguageModelSecret = "languagemodel";
    public static readonly TimeSpan MinRecording = TimeSpan.FromMilliseconds(350);

    private sealed class Session
    {
        public required HotkeyBinding Binding { get; set; }
        public required ModeDefinition Mode { get; set; }
        public required bool IsNote { get; init; }
        public required ForegroundInfo Target { get; init; }
        public required string AudioPath { get; init; }
        public required AppSettings Settings { get; init; }
        public CancellationTokenSource Cts { get; } = new();
    }

    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly SettingsStore _settingsStore = new(AppPaths.Settings);
    private readonly KeyboardHook _hook = new();
    private readonly AudioRecorder _recorder = new();
    private readonly TextInserter _inserter = new();
    private readonly RecordingIndicator _indicator = new();
    private TrayIcon? _tray;

    private ITranscriber? _transcriber;
    private string? _transcriberKey;
    private Session? _recording;
    private Session? _processing;
    private DictationResult? _last;
    private ReleaseInfo? _update;
    private LibraryWindow? _library;
    private SettingsWindow? _settingsWindow;

    public AppController(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
        Settings = _settingsStore.Load();
        Strings.SetLanguage(Settings.UiLanguage);
        var protector = new DpapiProtector();
        Secrets = new SecretStore(AppPaths.Secrets, protector);
        History = new HistoryStore(AppPaths.History, protector);
        Pending = new PendingRecordings(AppPaths.Pending);
        Http = CreateHttpClient();
        ModelDownloader = new ModelDownloader(Http, AppPaths.Models);
    }

    public AppSettings Settings { get; private set; }
    public SecretStore Secrets { get; }
    public HistoryStore History { get; }
    public PendingRecordings Pending { get; }
    public HttpClient Http { get; }
    public ModelDownloader ModelDownloader { get; }

    public static Version CurrentVersion =>
        typeof(AppController).Assembly.GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(0, 1, 0);

    /// <summary>Gepoolte, warm gehaltene Verbindung (spart TLS-Handshake bei Cloud-Diktaten).</summary>
    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            AutomaticDecompression = DecompressionMethods.All,
        };
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    // ── Start ──────────────────────────────────────────────────────────────────

    public void Start()
    {
        try { WhisperRuntime.Prepare(AppPaths.Local); }
        catch (Exception ex) { Log.Error("Whisper runtime", ex); }
        Pending.Cleanup();
        CleanupStaleRecordings();
        History.Prune(Settings.History.MaxEntries, Settings.History.MaxAgeDays);

        _indicator.Clicked += () => _ = StopAsync();
        _recorder.Level += level => Dispatcher.UIThread.Post(() => _indicator.SetLevel(level));
        _hook.HotkeyTriggered += ev => Dispatcher.UIThread.Post(() => OnHotkey(ev));
        _hook.Matcher = new HotkeyMatcher(Settings.BuildHotkeyBindings());
        try
        {
            _hook.Install();
        }
        catch (Exception ex)
        {
            Log.Error("Hook", ex);
            Toast.Show(Strings.Format("toast.hook_failed", ex.Message), isError: true);
        }

        CreateTray();
        PreloadTranscriber();

        if (!Settings.FirstRunCompleted)
            new SetupWindow(this).Show();
        else if (!Program.StartInBackground)
            Toast.Show(Strings.Format("tray.tooltip", Settings.DictateHotkey));

        if (Settings.CheckForUpdates) _ = CheckForUpdatesAsync();
    }

    private static void CleanupStaleRecordings()
    {
        foreach (var f in Directory.EnumerateFiles(AppPaths.Recordings, "*.wav"))
        {
            try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // ── Tastenkürzel ───────────────────────────────────────────────────────────

    private void OnHotkey(HotkeyEvent ev)
    {
        switch (ev.Kind)
        {
            case HotkeyEventKind.Start when ev.Binding is not null:
                StartRecording(ev.Binding);
                break;
            case HotkeyEventKind.Switch when ev.Binding is not null:
                SwitchMode(ev.Binding);
                break;
            case HotkeyEventKind.Stop:
                _ = StopAsync();
                break;
            case HotkeyEventKind.Cancel:
                _ = CancelAsync();
                break;
            case HotkeyEventKind.ToggleLibrary:
                ToggleLibrary();
                break;
        }
    }

    private ModeDefinition ResolveMode(HotkeyBinding binding, ForegroundInfo target, AppSettings s)
    {
        if (binding.Action == HotkeyAction.Note) return s.FindMode(s.Notes.ModeId) ?? s.DefaultMode;
        if (binding.ModeId is not null && s.FindMode(binding.ModeId) is { } m) return m;
        return s.FindMode(AppRuleResolver.ResolveModeId(s, target.App)) ?? s.DefaultMode;
    }

    private void StartRecording(HotkeyBinding binding)
    {
        if (_recording is not null || _processing is not null)
        {
            _hook.Matcher?.EndSession();
            return;
        }

        var settings = Settings;
        var isNote = binding.Action == HotkeyAction.Note;
        if (isNote && string.IsNullOrWhiteSpace(settings.Notes.Folder))
        {
            _hook.Matcher?.EndSession();
            Toast.Show(Strings.Get("library.no_folder"), isError: true);
            return;
        }

        if (EnsureTranscriber() is null)
        {
            _hook.Matcher?.EndSession();
            Toast.Show(Strings.Get("toast.no_model"), isError: true);
            OpenSettings("speech");
            return;
        }

        var target = ForegroundWindow.Capture();
        var session = new Session
        {
            Binding = binding,
            Mode = ResolveMode(binding, target, settings),
            IsNote = isNote,
            Target = target,
            AudioPath = Path.Combine(AppPaths.Recordings, $"rec-{Guid.NewGuid():N}.wav"),
            Settings = settings,
        };

        try
        {
            _recorder.Start(session.AudioPath, settings.Transcription.MicrophoneDevice);
        }
        catch (Exception ex)
        {
            Log.Error("Recorder", ex);
            _hook.Matcher?.EndSession();
            Toast.Show(Strings.Format("toast.mic_failed", ex.Message), isError: true);
            return;
        }

        _recording = session;
        SetTrayState("rec");
        if (settings.ShowRecordingIndicator) _indicator.ShowRecording(session.Mode, isNote);
    }

    private void SwitchMode(HotkeyBinding binding)
    {
        if (_recording is not { } s || s.IsNote) return;
        s.Binding = binding;
        s.Mode = ResolveMode(binding, s.Target, s.Settings);
        if (s.Settings.ShowRecordingIndicator) _indicator.ShowRecording(s.Mode, false);
    }

    private async Task CancelAsync()
    {
        if (_recording is not null)
        {
            _recording = null;
            _hook.Matcher?.EndSession();
            await _recorder.CancelAsync();
            ResetUi();
        }
        else
        {
            _processing?.Cts.Cancel();
        }
    }

    private async Task StopAsync()
    {
        if (_recording is not { } s) return;
        _recording = null;
        _hook.Matcher?.EndSession();

        string? path;
        try
        {
            path = await _recorder.StopAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Recorder stop", ex);
            Toast.Show(Strings.Format("toast.mic_failed", ex.Message), isError: true);
            ResetUi();
            return;
        }

        if (path is null || (WavFile.GetDuration(path) ?? TimeSpan.Zero) < MinRecording)
        {
            TryDelete(path);
            ResetUi();
            return;
        }

        _processing = s;
        SetTrayState("busy");
        if (s.Settings.ShowRecordingIndicator) _indicator.ShowProcessing();

        var keep = false;
        try
        {
            var transcriber = EnsureTranscriber() ?? throw new InvalidOperationException("no_transcriber");
            var pipeline = new DictationPipeline(transcriber, CreateChatClient(s.Settings));
            var result = await Task.Run(() => pipeline.RunAsync(path, s.Mode, s.Settings, s.Cts.Token));
            await DeliverAsync(s, result);
        }
        catch (OperationCanceledException)
        {
            // Esc während der Verarbeitung
        }
        catch (TranscriptionFailedException ex)
        {
            Log.Error("Transcription", ex.InnerException);
            Pending.Keep(path, s.Mode.Id, ex.Message);
            keep = true;
            Toast.Show(Strings.Format("toast.transcription_failed", Short(ex.Message)), isError: true);
            RebuildTrayMenu();
        }
        catch (Exception ex)
        {
            Log.Error("Dictation", ex);
            Toast.Show(Strings.Format("setup.error", Short(ex.Message)), isError: true);
        }
        finally
        {
            _processing = null;
            if (!keep) TryDelete(path);
            ResetUi();
        }
    }

    private async Task DeliverAsync(Session s, DictationResult result)
    {
        if (result.IsEmpty)
        {
            Toast.Show(Strings.Get("toast.nothing_heard"));
            return;
        }

        if (s.IsNote)
        {
            new NoteStore(s.Settings.Notes.Folder).Append(DateTime.Now, result.Final);
            if (s.Settings.Notes.CopyToClipboard) await ClipboardService.SetTextAsync(result.Final);
            Toast.Show(Strings.Get("toast.note_saved"));
            _library?.Refresh();
        }
        else
        {
            var method = AppRuleResolver.ResolveInsertion(s.Settings, s.Target.App);
            var outcome = await _inserter.InsertAsync(result.Final, method, s.Target, s.Settings.Insertion);
            switch (outcome)
            {
                case InsertOutcome.ClipboardOnly when method != InsertionMethod.ClipboardOnly:
                    Toast.Show(Strings.Get("toast.clipboard_only"));
                    break;
                case InsertOutcome.ClipboardOnly:
                    Toast.Show(Strings.Get("toast.copied"));
                    break;
                case InsertOutcome.Blocked:
                    Toast.Show(Strings.Get("toast.blocked"), isError: true);
                    break;
                case InsertOutcome.Failed:
                    Toast.Show(Strings.Get("toast.insert_failed"), isError: true);
                    break;
            }
        }

        switch (result.Warning)
        {
            case DictationWarning.ModeFailed:
                Toast.Show(Strings.Format("toast.mode_failed", Short(result.WarningDetail)), isError: true);
                break;
            case DictationWarning.ModeRejected:
                Toast.Show(Strings.Get("toast.mode_rejected"), isError: true);
                break;
            case DictationWarning.ModeNotConfigured:
                Toast.Show(Strings.Get("toast.mode_not_configured"), isError: true);
                break;
        }

        _last = result;
        if (s.Settings.History.Enabled)
        {
            History.Add(new HistoryEntry(DateTimeOffset.Now, result.Mode.Id, result.Verbatim, result.Final, s.Target.App.ProcessName),
                s.Settings.History.MaxEntries, s.Settings.History.MaxAgeDays);
        }
        RebuildTrayMenu();
    }

    private void ResetUi()
    {
        _indicator.HideIndicator();
        SetTrayState("idle");
    }

    private static string Short(string? s) => s is null ? "" : s.Length > 120 ? s[..120] + "…" : s;

    private static void TryDelete(string? path)
    {
        if (path is null) return;
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // ── Dienste ────────────────────────────────────────────────────────────────

    private string TranscriberKey(AppSettings s) => s.Transcription.Engine == TranscriptionEngine.Local
        ? $"local|{s.Transcription.LocalModelId}|{s.Transcription.UseGpu}"
        : $"cloud|{s.Transcription.Cloud.BaseUrl}|{s.Transcription.Cloud.Model}|{Secrets.Load(TranscriptionSecret).GetHashCode()}";

    /// <summary>Liefert den passenden Transkribierer oder null, wenn (noch) keiner nutzbar ist.</summary>
    public ITranscriber? EnsureTranscriber()
    {
        var s = Settings;
        var key = TranscriberKey(s);
        if (_transcriber is not null && key == _transcriberKey) return _transcriber;

        (_transcriber as IDisposable)?.Dispose();
        _transcriber = null;
        _transcriberKey = null;

        try
        {
            if (s.Transcription.Engine == TranscriptionEngine.Local)
            {
                var model = WhisperModelCatalog.Find(s.Transcription.LocalModelId);
                if (model is null || !ModelDownloader.IsInstalled(model)) return null;
                _transcriber = new LocalWhisperTranscriber(ModelDownloader.PathFor(model), s.Transcription.UseGpu);
            }
            else
            {
                var c = s.Transcription.Cloud;
                _transcriber = new OpenAiCompatibleTranscriber(Http, c.BaseUrl, c.Model, Secrets.Load(TranscriptionSecret));
            }
            _transcriberKey = key;
            return _transcriber;
        }
        catch (Exception ex)
        {
            Log.Error("Transcriber", ex);
            return null;
        }
    }

    private void PreloadTranscriber()
    {
        if (EnsureTranscriber() is LocalWhisperTranscriber local)
        {
            _ = Task.Run(() =>
            {
                try { local.Preload(); Log.Info("Whisper runtime: " + LocalWhisperTranscriber.LoadedRuntime); }
                catch (Exception ex) { Log.Error("Preload", ex); }
            });
        }
    }

    public IChatClient? CreateChatClient(AppSettings s)
    {
        var e = s.LanguageModel.Endpoint;
        if (string.IsNullOrWhiteSpace(e.BaseUrl) || string.IsNullOrWhiteSpace(e.Model)) return null;
        try
        {
            return new OpenAiCompatibleChatClient(Http, e.BaseUrl, e.Model, Secrets.Load(LanguageModelSecret))
            {
                // lokale Modelle laden beim ersten Aufruf ggf. erst in den Speicher
                Timeout = e.IsLocal ? TimeSpan.FromSeconds(120) : TimeSpan.FromSeconds(60),
            };
        }
        catch (Exception ex)
        {
            Log.Error("Chat client", ex);
            return null;
        }
    }

    /// <summary>Neue Einstellungen übernehmen (aus dem Einstellungsfenster oder der Einrichtung).</summary>
    public void ApplySettings(AppSettings updated)
    {
        updated.Normalize();
        Settings = updated;
        _settingsStore.Save(updated);
        _hook.Matcher = new HotkeyMatcher(updated.BuildHotkeyBindings());
        try
        {
            if (Autostart.IsEnabled != updated.Autostart) Autostart.Set(updated.Autostart);
        }
        catch (Exception ex)
        {
            Log.Error("Autostart", ex);
        }
        History.Prune(updated.History.MaxEntries, updated.History.MaxAgeDays);
        if (!updated.History.Enabled) History.Clear();
        PreloadTranscriber();
        RebuildTrayMenu();
        _library?.ApplySettings(updated);
    }

    /// <summary>Kopie der aktuellen Einstellungen zum Bearbeiten.</summary>
    public AppSettings CloneSettings() =>
        System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(Settings, SettingsStore.JsonOptions), SettingsStore.JsonOptions)!;

    // ── Fenster ────────────────────────────────────────────────────────────────

    public void OpenSettings(string? tab = null)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        _settingsWindow.SelectTab(tab);
        _settingsWindow.Activate();
    }

    public void ToggleLibrary()
    {
        if (_library is { IsVisible: true })
        {
            _library.Close();
            return;
        }
        _library = new LibraryWindow(Settings);
        _library.Closed += (_, _) => _library = null;
        _library.Show();
        _library.Activate();
    }

    // ── Tray ───────────────────────────────────────────────────────────────────

    private void CreateTray()
    {
        _tray = new TrayIcon { Icon = LoadIcon("idle"), IsVisible = true };
        _tray.Clicked += (_, _) => OpenSettings();
        TrayIcon.SetIcons(Application.Current!, [_tray]);
        RebuildTrayMenu();
    }

    private static WindowIcon LoadIcon(string state) =>
        new(AssetLoader.Open(new Uri($"avares://SonicScript/Assets/tray-{state}.png")));

    private void SetTrayState(string state)
    {
        if (_tray is not null) _tray.Icon = LoadIcon(state);
    }

    private void RebuildTrayMenu()
    {
        if (_tray is null) return;
        _tray.ToolTipText = Strings.Format("tray.tooltip", Settings.DictateHotkey);

        var menu = new NativeMenu();

        var copyLast = new NativeMenuItem(Strings.Get("tray.copy_last")) { IsEnabled = _last is not null };
        copyLast.Click += async (_, _) =>
        {
            if (_last is null) return;
            await ClipboardService.SetTextAsync(_last.Final);
            Toast.Show(Strings.Get("toast.copied"));
        };
        menu.Add(copyLast);

        if (_last is { } last && last.Final != last.Verbatim)
        {
            var verbatim = new NativeMenuItem(Strings.Get("tray.copy_verbatim"));
            verbatim.Click += async (_, _) =>
            {
                await ClipboardService.SetTextAsync(last.Verbatim);
                Toast.Show(Strings.Get("toast.copied"));
            };
            menu.Add(verbatim);
        }

        menu.Add(new NativeMenuItemSeparator());

        var library = new NativeMenuItem($"{Strings.Get("tray.library")}  ({Settings.Notes.LibraryHotkey})");
        library.Click += (_, _) => ToggleLibrary();
        menu.Add(library);

        var pendingList = Pending.List();
        var pending = new NativeMenuItem($"{Strings.Get("tray.pending")} ({pendingList.Count})") { Menu = new NativeMenu() };
        if (pendingList.Count == 0)
        {
            pending.Menu.Add(new NativeMenuItem(Strings.Get("tray.pending.none")) { IsEnabled = false });
        }
        foreach (var rec in pendingList)
        {
            var item = new NativeMenuItem($"{rec.CreatedAt.ToLocalTime():dd.MM. HH:mm} – {Settings.FindMode(rec.ModeId)?.Name ?? rec.ModeId}");
            item.Click += async (_, _) => await RetranscribeAsync(rec);
            pending.Menu.Add(item);
        }
        menu.Add(pending);

        menu.Add(new NativeMenuItemSeparator());

        if (_update is { } upd)
        {
            var update = new NativeMenuItem(Strings.Format("tray.update", upd.Tag));
            update.Click += (_, _) => OpenUrl(upd.HtmlUrl);
            menu.Add(update);
        }

        var settings = new NativeMenuItem(Strings.Get("tray.settings"));
        settings.Click += (_, _) => OpenSettings();
        menu.Add(settings);

        var quit = new NativeMenuItem(Strings.Get("tray.quit"));
        quit.Click += (_, _) => _lifetime.Shutdown();
        menu.Add(quit);

        _tray.Menu = menu;
    }

    private async Task RetranscribeAsync(PendingRecording rec)
    {
        var transcriber = EnsureTranscriber();
        if (transcriber is null)
        {
            Toast.Show(Strings.Get("toast.no_model"), isError: true);
            return;
        }
        SetTrayState("busy");
        try
        {
            var s = Settings;
            var mode = s.FindMode(rec.ModeId) ?? s.DefaultMode;
            var pipeline = new DictationPipeline(transcriber, CreateChatClient(s));
            var result = await Task.Run(() => pipeline.RunAsync(rec.AudioPath, mode, s, CancellationToken.None));
            await ClipboardService.SetTextAsync(result.Final);
            _last = result;
            if (s.History.Enabled)
                History.Add(new HistoryEntry(DateTimeOffset.Now, mode.Id, result.Verbatim, result.Final, null), s.History.MaxEntries, s.History.MaxAgeDays);
            Pending.Remove(rec);
            Toast.Show(Strings.Get("toast.retranscribed"));
        }
        catch (Exception ex)
        {
            Log.Error("Retranscribe", ex);
            Toast.Show(Strings.Format("toast.transcription_failed", Short(ex.Message)), isError: true);
        }
        finally
        {
            SetTrayState("idle");
            RebuildTrayMenu();
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15));
            _update = await new UpdateChecker(Http).GetNewerReleaseAsync(CurrentVersion, CancellationToken.None);
            if (_update is not null)
            {
                RebuildTrayMenu();
                Toast.Show(Strings.Format("tray.update", _update.Tag));
            }
        }
        catch (Exception ex)
        {
            Log.Error("Update check", ex);
        }
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Open url", ex); }
    }

    public void Dispose()
    {
        _hook.Dispose();
        _recorder.Dispose();
        (_transcriber as IDisposable)?.Dispose();
        _tray?.Dispose();
        Http.Dispose();
    }
}
