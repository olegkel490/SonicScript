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
using SonicScript.Core.Net;
using SonicScript.Core.Settings;
using SonicScript.Core.Stats;
using SonicScript.Core.Storage;
using SonicScript.Core.Text;
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
        public required bool IsEdit { get; init; }
        /// <summary>Text vor dem Cursor / Auswahl / Passwortfeld – wird beim Start im Hintergrund gelesen.</summary>
        public required Task<FocusContext?> Context { get; init; }
        /// <summary>Länger als die Antipp-Schwelle gehalten → echte Aufnahme (Ton, Ducking, Anzeige).</summary>
        public bool Confirmed { get; set; }
        public required ForegroundInfo Target { get; init; }
        public required string AudioPath { get; init; }
        public required AppSettings Settings { get; init; }
        public CancellationTokenSource Cts { get; } = new();
        /// <summary>Erkennung während der Aufnahme (Teilstücke + Vorschau).</summary>
        public LiveTranscription? Live { get; set; }
        public TimeSpan AudioDuration { get; set; }
    }

    /// <summary>Zuletzt eingefügter Text – für "Rückgängig".</summary>
    private sealed record LastInsert(ForegroundInfo Target, string Final, string? Verbatim, string? Before, DateTime At, bool VerbatimShown);

    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly SettingsStore _settingsStore = new(AppPaths.Settings);
    private readonly KeyboardHook _hook = new();
    private readonly AudioRecorder _recorder = new();
    private readonly TextInserter _inserter = new();
    private readonly RecordingIndicator _indicator = new();
    private readonly AudioDucker _ducker = new();
    private ModeBadge? _badge;
    private ModeSwitchOsd? _osd;
    private string _trayState = "idle";
    private TrayIcon? _tray;

    private ITranscriber? _transcriber;
    private string? _transcriberKey;
    private Session? _recording;
    private Session? _processing;
    private DictationResult? _last;
    private LastInsert? _lastInsert;
    private LiveTranscription? _liveSink;
    private CancellationTokenSource? _correctionCts;
    private DateTime _costCheckedAt = DateTime.MinValue;
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
        Stats = new UsageStats(AppPaths.Stats);
        Http = CreateHttpClient();
        ModelDownloader = new ModelDownloader(Http, AppPaths.Models);
    }

    public AppSettings Settings { get; private set; }
    public SecretStore Secrets { get; }
    public HistoryStore History { get; }
    public PendingRecordings Pending { get; }
    public UsageStats Stats { get; }
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
        Log.Info($"Start {CurrentVersion} on {Environment.OSVersion}");
        WhisperRuntime.StartInBackground(AppPaths.Local);
        Pending.Cleanup();
        CleanupStaleRecordings();
        History.Prune(Settings.History.MaxEntries, Settings.History.MaxAgeDays);

        _indicator.Clicked += () => _ = StopAsync();
        _recorder.Level += level => Dispatcher.UIThread.Post(() => _indicator.SetLevel(level));
        _recorder.Samples += samples => Volatile.Read(ref _liveSink)?.Append(samples);
        _hook.HotkeyTriggered += ev => Dispatcher.UIThread.Post(() => OnHotkey(ev));
        Controls.HotkeyCapture.ActiveChanged += active => _hook.Suspended = active;
        _hook.Matcher = new HotkeyMatcher(Settings.BuildHotkeyBindings());
        try
        {
            _hook.Install();
            Log.Info("Keyboard hook installed");
        }
        catch (Exception ex)
        {
            Log.Error("Hook", ex);
            Toast.Show(Strings.Format("toast.hook_failed", ex.Message), isError: true);
        }

        CreateTray();
        Log.Info("Tray created");
        PreloadTranscriber();

        if (Program.AfterUpdate)
            Toast.Show(Strings.Format("toast.updated", CurrentVersion.ToString(3)));

        if (!Settings.FirstRunCompleted)
        {
            ShowMainWindow();
            Log.Info("Setup window shown");
        }
        else if (!Program.StartInBackground)
        {
            Toast.Show(Strings.Format("tray.tooltip", Settings.DictateHotkey));
        }

        _ = UpdateLoopAsync();
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
            case HotkeyEventKind.NextMode:
                StepMode(+1);
                break;
            case HotkeyEventKind.PreviousMode:
                StepMode(-1);
                break;
            case HotkeyEventKind.Undo:
                _ = UndoLastAsync();
                break;
        }
    }

    private ModeDefinition ResolveMode(HotkeyBinding binding, ForegroundInfo target, AppSettings s, CycleEntry? entry)
    {
        if (binding.Action == HotkeyAction.Note || entry?.Kind == CycleKind.Note) return s.FindMode(s.Notes.ModeId) ?? s.DefaultMode;
        if (binding.Action == HotkeyAction.EditSelection || entry?.Kind == CycleKind.Edit) return DictationPipeline.EditMode;
        if (binding.ModeId is not null && s.FindMode(binding.ModeId) is { } m) return m;
        if (s.AppRulesSelectMode && AppRuleResolver.Match(s.AppRules, target.App) is { } rule && s.FindMode(rule.ModeId) is { } ruleMode)
            return ruleMode;
        return entry?.Mode ?? s.DefaultMode;
    }

    /// <summary>Diktat-Taste ohne festen Modus → aktuell gewählter Eintrag des Moduswechsels.</summary>
    private static CycleEntry? EntryFor(HotkeyBinding binding, AppSettings s) =>
        binding.Action == HotkeyAction.Dictate && binding.ModeId is null ? s.CurrentEntry : null;

    /// <summary>Moduswechsel per Taste oder Abzeichen.</summary>
    public void StepMode(int direction)
    {
        var entry = Settings.StepMode(direction);
        SaveQuietly();
        RefreshModeDisplay();
        ShowModeSwitch(entry);
        RebuildTrayMenu();
    }

    /// <summary>Kurze Einblendung beim Wechsel (Variante C).</summary>
    private void ShowModeSwitch(CycleEntry entry)
    {
        try
        {
            _osd ??= new ModeSwitchOsd();
            _osd.ShowEntry(entry, Settings.BuildCycle(),
                Controls.HotkeyBox.Display(Settings.NextModeHotkey), Controls.HotkeyBox.Display(Settings.PrevModeHotkey));
        }
        catch (Exception ex)
        {
            Log.Error("Mode OSD", ex);
            Toast.Show(Strings.Format("toast.mode", entry.Name));
        }
    }

    public void SelectMode(string id)
    {
        Settings.CurrentModeId = id;
        Settings.Normalize();
        SaveQuietly();
        RefreshModeDisplay();
        RebuildTrayMenu();
    }

    private void SaveQuietly()
    {
        try { _settingsStore.Save(Settings); }
        catch (Exception ex) { Log.Error("Save settings", ex); }
    }

    /// <summary>Tray-Symbol, Tooltip und Abzeichen auf den aktuellen Modus/Zustand bringen.</summary>
    private void RefreshModeDisplay()
    {
        var entry = Settings.CurrentEntry;
        if (_tray is not null)
        {
            try { _tray.Icon = ModeIcons.Get(entry.Symbol, _trayState); }
            catch (Exception ex) { Log.Error("Tray icon", ex); _tray.Icon = LoadIcon(_trayState); }
            _tray.ToolTipText = Strings.Format("tray.tooltip_mode", entry.Name, Controls.HotkeyBox.Display(Settings.DictateHotkey));
        }
        _badge?.Update(entry, _trayState);
    }

    private void UpdateBadgeVisibility()
    {
        if (Settings.ShowModeBadge)
        {
            if (_badge is null) CreateBadge();
            RefreshModeDisplay();
        }
        else
        {
            CloseBadge();
        }
    }

    private bool _disposing;

    private void CreateBadge()
    {
        var badge = new ModeBadge();
        badge.NextClicked += () => StepMode(+1);
        badge.PreviousClicked += () => StepMode(-1);
        badge.Closed += (_, _) =>
        {
            // Unerwartet geschlossen (nicht von uns)? Dann neu anlegen.
            if (_badge == badge && !_disposing)
            {
                _badge = null;
                _ = RecreateBadgeLaterAsync();
            }
        };
        _badge = badge;
        badge.Update(Settings.CurrentEntry, _trayState);
        badge.Show();
    }

    private void CloseBadge()
    {
        var b = _badge;
        _badge = null;
        b?.Close();
    }

    private async Task RecreateBadgeLaterAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(3));
        if (!_disposing && _badge is null && Settings.ShowModeBadge) CreateBadge();
    }


    private void StartRecording(HotkeyBinding binding)
    {
        if (_recording is not null || _processing is not null)
        {
            _hook.Matcher?.EndSession();
            return;
        }

        var settings = Settings;
        var entry = EntryFor(binding, settings);
        var isNote = binding.Action == HotkeyAction.Note || entry?.Kind == CycleKind.Note;
        if (isNote && string.IsNullOrWhiteSpace(settings.Notes.Folder))
        {
            _hook.Matcher?.EndSession();
            Toast.Show(Strings.Get("library.no_folder"), isError: true);
            return;
        }

        var transcriber = EnsureTranscriber();
        if (transcriber is null)
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
            Mode = ResolveMode(binding, target, settings, entry),
            IsNote = isNote,
            IsEdit = binding.Action == HotkeyAction.EditSelection || entry?.Kind == CycleKind.Edit,
            Context = FocusContextReader.CaptureAsync(TimeSpan.FromMilliseconds(600)),
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

        session.Live = CreateLive(transcriber, settings);
        Volatile.Write(ref _liveSink, session.Live);
        _recording = session;
        _correctionCts?.Cancel();
        SetTrayState("rec");
        _ = ConfirmRecordingAsync(session);
    }

    /// <summary>
    /// Anzeige, Ton und Ducking erst nach der Antipp-Schwelle: Wird z. B. Strg+- nur kurz getippt (= Herauszoomen),
    /// passiert nichts Sichtbares oder Hörbares.
    /// </summary>
    private LiveTranscription? CreateLive(ITranscriber transcriber, AppSettings s)
    {
        try
        {
            var live = new LiveTranscription(transcriber, 16000, new LiveTranscriptionOptions
            {
                WorkDirectory = AppPaths.Recordings,
                Preview = s.LivePreview && s.Transcription.Engine == TranscriptionEngine.Local,
                TrimSilence = s.TrimSilence,
                Language = s.Transcription.Language == "auto" ? null : s.Transcription.Language,
                Prompt = DictationPipeline.BuildWhisperPrompt(s.Dictionary.Terms),
            });
            live.PreviewChanged += text => Dispatcher.UIThread.Post(() =>
            {
                if (_recording?.Live == live && _recording.Confirmed && _recording.Settings.ShowRecordingIndicator) _indicator.SetPreview(text);
            });
            return live;
        }
        catch (Exception ex)
        {
            Log.Error("Live transcription", ex);
            return null;
        }
    }

    private void StopLiveSink(Session s)
    {
        if (Volatile.Read(ref _liveSink) == s.Live) Volatile.Write(ref _liveSink, null);
    }

    private async Task ConfirmRecordingAsync(Session session)
    {
        await Task.Delay(HotkeyMatcher.TapThreshold);
        if (_recording != session) return;
        session.Confirmed = true;
        var s = session.Settings;
        if (s.PlaySounds) Sounds.Play(Cue.Start);
        if (s.DuckOtherAudio) _ducker.Duck(s.DuckLevelPercent);
        if (s.ShowRecordingIndicator) _indicator.ShowRecording(session.Mode, session.IsNote);
    }

    /// <summary>Ende der Aufnahme: Ducking aufheben, ggf. Ton.</summary>
    private void EndRecordingFeedback(Session session, Cue cue)
    {
        if (!session.Confirmed) return;
        if (session.Settings.DuckOtherAudio) _ducker.Restore();
        if (session.Settings.PlaySounds) Sounds.Play(cue);
    }

    private void SwitchMode(HotkeyBinding binding)
    {
        if (_recording is not { } s || s.IsNote) return;
        s.Binding = binding;
        s.Mode = ResolveMode(binding, s.Target, s.Settings, EntryFor(binding, s.Settings));
        if (s.Settings.ShowRecordingIndicator) _indicator.ShowRecording(s.Mode, false);
    }

    private async Task CancelAsync()
    {
        if (_recording is { } rec)
        {
            _recording = null;
            _hook.Matcher?.EndSession();
            EndRecordingFeedback(rec, Cue.Cancel);
            StopLiveSink(rec);
            rec.Live?.Dispose();
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
        EndRecordingFeedback(s, Cue.Stop);
        StopLiveSink(s);

        string? path;
        try
        {
            path = await _recorder.StopAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Recorder stop", ex);
            Toast.Show(Strings.Format("toast.mic_failed", ex.Message), isError: true);
            s.Live?.Dispose();
            ResetUi();
            return;
        }

        s.AudioDuration = path is null ? TimeSpan.Zero : WavFile.GetDuration(path) ?? TimeSpan.Zero;
        if (path is null || s.AudioDuration < MinRecording)
        {
            s.Live?.Dispose();
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
            var context = await s.Context;
            var (live, transcript) = await FinishLiveAsync(s);
            if (s.IsEdit)
            {
                await EditSelectionAsync(s, pipeline, path, context, live, transcript);
            }
            else
            {
                var allowKeywords = s.Binding.Action == HotkeyAction.Dictate;
                var (replyTo, replyInClipboard) = await FindMailAsync(s, context);
                var result = live
                    ? await Task.Run(() => pipeline.RunTranscriptAsync(transcript, s.Mode, s.Settings, s.Cts.Token, allowKeywords, replyTo))
                    : await Task.Run(() => pipeline.RunAsync(path, s.Mode, s.Settings, s.Cts.Token, allowKeywords, replyTo));
                await DeliverAsync(s, result, context, replyInClipboard, replyTo is not null);
            }
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
            if (s.Settings.PlaySounds) Sounds.Play(Cue.Error);
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
            s.Live?.Dispose();
            _processing = null;
            if (!keep) TryDelete(path);
            ResetUi();
        }
    }

    /// <summary>
    /// Ergebnis der Live-Erkennung. (false, _) = nicht verfügbar oder fehlgeschlagen → die Datei wird wie bisher
    /// komplett erkannt (scheitert auch das, bleibt die Aufnahme erhalten).
    /// </summary>
    private static async Task<(bool Ok, TranscriptionResult? Transcript)> FinishLiveAsync(Session s)
    {
        if (s.Live is not { } live) return (false, null);
        try
        {
            var tr = await Task.Run(() => live.FinishAsync(s.Cts.Token));
            if (live.CommittedChunks > 0) Log.Info($"Live transcription: {live.CommittedChunks} chunk(s) during recording");
            return (true, tr);
        }
        catch (OperationCanceledException) when (s.Cts.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error("Live transcription", ex);
            return (false, null);
        }
        finally
        {
            live.Dispose();
        }
    }

    /// <summary>
    /// Für den Antwort-Modus die E-Mail finden: Markierung (Antwort kommt dann in die Zwischenablage) oder der Text
    /// unter dem Cursor (zitierte Mail im Antwortfenster; Antwort wird eingefügt). Bei Sprachbefehlen nur, was ohne
    /// Strg+C verfügbar ist.
    /// </summary>
    private static async Task<(string? Mail, bool ToClipboard)> FindMailAsync(Session s, FocusContext? context)
    {
        var isReply = s.Mode.Kind == ModeKind.Reply;
        if (!isReply && !(s.Settings.VoiceKeywords && s.Binding.Action == HotkeyAction.Dictate)) return (null, false);
        if (context?.SelectedText is { Length: >= 20 } sel) return (sel, true);
        if (context?.TextAfter is { } after && after.Trim().Length >= 40) return (after, false);
        if (isReply && ForegroundWindow.Current == s.Target.Handle && await TextInserter.CopySelectionAsync() is { Length: >= 20 } copied)
            return (copied, true);
        return (null, false);
    }

    /// <summary>"Auswahl bearbeiten": Markierung holen, Anweisung anwenden, Ergebnis ersetzt die Markierung.</summary>
    private async Task EditSelectionAsync(Session s, DictationPipeline pipeline, string audioPath, FocusContext? context, bool live, TranscriptionResult? transcript)
    {
        var selected = context?.SelectedText;
        if (string.IsNullOrEmpty(selected) && ForegroundWindow.Current == s.Target.Handle)
            selected = await TextInserter.CopySelectionAsync();
        if (string.IsNullOrEmpty(selected))
        {
            Toast.Show(Strings.Get("toast.no_selection"), isError: true);
            return;
        }

        var result = live
            ? await Task.Run(() => pipeline.RunEditTranscriptAsync(transcript, selected, s.Settings, s.Cts.Token))
            : await Task.Run(() => pipeline.RunEditAsync(audioPath, selected, s.Settings, s.Cts.Token));
        if (result.IsEmpty)
        {
            ShowWarning(result);
            if (result.Warning == DictationWarning.None) Toast.Show(Strings.Get("toast.nothing_heard"));
            return;
        }

        // Die Markierung ist noch aktiv → Einfügen ersetzt sie.
        var method = AppRuleResolver.ResolveInsertion(s.Settings, s.Target.App);
        if (method == InsertionMethod.Type) method = InsertionMethod.Paste;
        var outcome = await _inserter.InsertAsync(result.Final, method, s.Target, s.Settings.Insertion);
        ReportOutcome(outcome, method);
        _lastInsert = outcome == InsertOutcome.Inserted ? new LastInsert(s.Target, result.Final, null, null, DateTime.UtcNow, false) : null;
        Remember(s, result with { Verbatim = selected }, result.Verbatim);
    }

    private async Task DeliverAsync(Session s, DictationResult result, FocusContext? context, bool replyInClipboard = false, bool foundMail = false)
    {
        if (result.IsEmpty)
        {
            Toast.Show(Strings.Get("toast.nothing_heard"));
            return;
        }

        var asNote = s.IsNote || result.SaveAsNote;
        if (asNote && string.IsNullOrWhiteSpace(s.Settings.Notes.Folder))
        {
            // "Notiz: …" ohne Notizordner → nicht verlieren, sondern in die Zwischenablage.
            await ClipboardService.SetTextAsync(result.Final);
            Toast.Show(Strings.Get("library.no_folder") + " " + Strings.Get("toast.copied"), isError: true);
            Remember(s, result);
            return;
        }

        if (!asNote && result.Mode.Kind == ModeKind.Reply && replyInClipboard)
        {
            // Mail war markiert (z. B. im Lesebereich) → Markierung nicht überschreiben.
            await ClipboardService.SetTextAsync(result.Final);
            Toast.Show(Strings.Get("toast.reply_clipboard"));
            ShowWarning(result);
            Remember(s, result);
            return;
        }

        if (asNote)
        {
            new NoteStore(s.Settings.Notes.Folder).Append(DateTime.Now, result.Final);
            if (s.Settings.Notes.CopyToClipboard) await ClipboardService.SetTextAsync(result.Final);
            Toast.Show(Strings.Get("toast.note_saved"));
            _library?.Refresh();
        }
        else
        {
            var method = AppRuleResolver.ResolveInsertion(s.Settings, s.Target.App);
            var target = context?.IsPassword == true ? s.Target with { IsPasswordField = true } : s.Target;
            var text = s.Settings.Insertion.FitToContext ? InsertionFitter.Fit(result.Final, context?.TextBefore) : result.Final;
            var outcome = await _inserter.InsertAsync(text, method, target, s.Settings.Insertion);
            ReportOutcome(outcome, method);
            if (outcome == InsertOutcome.Inserted)
            {
                _lastInsert = new LastInsert(target, result.Final, result.Verbatim, context?.TextBefore, DateTime.UtcNow, false);
                WatchCorrections(s, text);
            }
            else
            {
                _lastInsert = null;
            }
            if (result.Mode.Kind == ModeKind.Reply && !foundMail && result.Warning == DictationWarning.None)
                Toast.Show(Strings.Get("toast.reply_no_mail"));
        }

        ShowWarning(result);
        Remember(s, result);
    }

    // ── Rückgängig ─────────────────────────────────────────────────────────────

    public static readonly TimeSpan UndoWindow = TimeSpan.FromMinutes(10);

    /// <summary>1. Druck: KI-Text durch die wortgetreue Fassung ersetzen. 2. Druck (oder ohne KI): Diktat entfernen.</summary>
    private async Task UndoLastAsync()
    {
        if (_recording is not null || _processing is not null) return;
        var li = _lastInsert;
        if (li is null || DateTime.UtcNow - li.At > UndoWindow || ForegroundWindow.Current != li.Target.Handle)
        {
            Toast.Show(Strings.Get("toast.undo_nothing"));
            return;
        }
        _correctionCts?.Cancel();

        if (!li.VerbatimShown && li.Verbatim is { Length: > 0 } verbatim && verbatim != li.Final)
        {
            if (!await TextInserter.UndoAsync(li.Target)) { Toast.Show(Strings.Get("toast.undo_failed"), isError: true); return; }
            await Task.Delay(120);
            var s = Settings;
            var text = s.Insertion.FitToContext ? InsertionFitter.Fit(verbatim, li.Before) : verbatim;
            var outcome = await _inserter.InsertAsync(text, InsertionMethod.Paste, li.Target, s.Insertion);
            if (outcome == InsertOutcome.Inserted)
            {
                _lastInsert = li with { VerbatimShown = true, At = DateTime.UtcNow };
                Toast.Show(Strings.Format("toast.undo_verbatim", Controls.HotkeyBox.Display(s.UndoHotkey)));
            }
            else
            {
                _lastInsert = null;
                ReportOutcome(outcome, InsertionMethod.Paste);
            }
            return;
        }

        if (await TextInserter.UndoAsync(li.Target))
        {
            _lastInsert = null;
            Toast.Show(Strings.Get("toast.undo_removed"));
        }
        else
        {
            Toast.Show(Strings.Get("toast.undo_failed"), isError: true);
        }
    }

    // ── Wörterbuch lernt mit ───────────────────────────────────────────────────

    private void WatchCorrections(Session s, string inserted)
    {
        _correctionCts?.Cancel();
        _correctionCts = null;
        if (!s.Settings.Dictionary.LearnCorrections || UsageStats.CountWords(inserted) < 3) return;
        var cts = new CancellationTokenSource();
        _correctionCts = cts;
        _ = WatchCorrectionsAsync(s.Target.Handle, inserted, cts.Token);
    }

    /// <summary>Nach 15 s, 45 s und 90 s nachsehen, ob im Feld einzelne Wörter korrigiert wurden.</summary>
    private async Task WatchCorrectionsAsync(IntPtr window, string inserted, CancellationToken ct)
    {
        foreach (var wait in new[] { 15, 30, 45 })
        {
            try { await Task.Delay(TimeSpan.FromSeconds(wait), ct); }
            catch (OperationCanceledException) { return; }
            if (ForegroundWindow.Current != window) continue;
            var text = await FocusContextReader.ReadFocusedTextAsync(TimeSpan.FromSeconds(1));
            if (ct.IsCancellationRequested) return;
            if (text is null) continue;
            var dict = Settings.Dictionary;
            var found = CorrectionDetector.Detect(inserted, text)
                .Where(c => !dict.IgnoredSuggestions.Contains(DictionarySettings.SuggestionKey(c.From, c.To)))
                .Where(c => !dict.Terms.Contains(c.To))
                .ToList();
            if (found.Count == 0) continue;
            OfferSuggestions(found);
            return;
        }
    }

    private void OfferSuggestions(IReadOnlyList<CorrectionSuggestion> found)
    {
        var list = string.Join(", ", found.Take(3).Select(c => $"„{c.From}“ → „{c.To}“")) + (found.Count > 3 ? " …" : "");
        Toast.Show(Strings.Format("toast.learn", list), false,
            Strings.Get("toast.learn_accept"), () =>
            {
                var updated = CloneSettings();
                foreach (var c in found) updated.Dictionary.Accept(c.From, c.To);
                ApplySettings(updated);
                Toast.Show(Strings.Get("toast.learn_added"));
            },
            Strings.Get("toast.learn_ignore"), () =>
            {
                var updated = CloneSettings();
                foreach (var c in found) updated.Dictionary.IgnoredSuggestions.Add(DictionarySettings.SuggestionKey(c.From, c.To));
                ApplySettings(updated);
            });
    }

    // ── Kosten (OpenRouter) ────────────────────────────────────────────────────

    /// <summary>Verbrauch aller OpenRouter-Schlüssel, die SonicScript nutzt. null = OpenRouter wird nicht genutzt.</summary>
    public async Task<OpenRouterKeyUsage?> GetOpenRouterUsageAsync(CancellationToken ct)
    {
        var s = Settings;
        var keys = new List<string>();
        if (s.Transcription.Engine == TranscriptionEngine.Cloud && OpenRouterUsage.IsOpenRouter(s.Transcription.Cloud.BaseUrl) &&
            Secrets.Load(TranscriptionSecret) is { Length: > 0 } k1)
            keys.Add(k1);
        if (OpenRouterUsage.IsOpenRouter(s.LanguageModel.Endpoint.BaseUrl) && Secrets.Load(LanguageModelSecret) is { Length: > 0 } k2 && !keys.Contains(k2))
            keys.Add(k2);
        if (keys.Count == 0) return null;
        var list = await Task.WhenAll(keys.Select(k => OpenRouterUsage.GetAsync(Http, k, ct)));
        return OpenRouterUsage.Sum(list);
    }

    /// <summary>Nach Diktaten höchstens alle 10 Minuten prüfen, ob das Monatslimit erreicht ist.</summary>
    private async Task CheckCostLimitAsync()
    {
        var s = Settings;
        var month = DateTime.Now.ToString("yyyy-MM");
        if (s.CostLimitMonthlyUsd <= 0 || s.CostWarnedMonth == month || DateTime.UtcNow - _costCheckedAt < TimeSpan.FromMinutes(10)) return;
        _costCheckedAt = DateTime.UtcNow;
        try
        {
            var u = await GetOpenRouterUsageAsync(CancellationToken.None);
            if (u?.Month is not { } used || used < s.CostLimitMonthlyUsd) return;
            Settings.CostWarnedMonth = month;
            SaveQuietly();
            Toast.Show(Strings.Format("toast.cost_limit", used.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                s.CostLimitMonthlyUsd.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)), isError: true);
        }
        catch (Exception ex)
        {
            Log.Error("Cost check", ex);
        }
    }

    private static void ReportOutcome(InsertOutcome outcome, InsertionMethod method)
    {
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

    private static void ShowWarning(DictationResult result)
    {
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
    }

    private void Remember(Session s, DictationResult result, string? spokenText = null)
    {
        _last = result;
        Stats.Add(DateOnly.FromDateTime(DateTime.Now), result.Mode.Id, spokenText ?? result.Final, s.AudioDuration);
        _ = CheckCostLimitAsync();
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
        UpdateBadgeVisibility();
        RefreshModeDisplay();
        RebuildTrayMenu();
        _library?.ApplySettings(updated);
    }

    /// <summary>Kopie der aktuellen Einstellungen zum Bearbeiten.</summary>
    public AppSettings CloneSettings() =>
        System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(Settings, SettingsStore.JsonOptions), SettingsStore.JsonOptions)!;

    // ── Fenster ────────────────────────────────────────────────────────────────

    private SetupWindow? _setupWindow;

    /// <summary>Einrichtung beim ersten Start, sonst Einstellungen (auch wenn SonicScript erneut gestartet wird).</summary>
    public void ShowMainWindow()
    {
        if (Settings.FirstRunCompleted)
        {
            OpenSettings();
            return;
        }
        if (_setupWindow is null)
        {
            _setupWindow = new SetupWindow(this);
            _setupWindow.Closed += (_, _) => _setupWindow = null;
            _setupWindow.Show();
        }
        _setupWindow.WindowState = WindowState.Normal;
        _setupWindow.Activate();
    }

    public void OpenSettings(string? tab = null)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        _settingsWindow.WindowState = WindowState.Normal;
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
        _tray.Clicked += (_, _) => ShowMainWindow();
        TrayIcon.SetIcons(Application.Current!, [_tray]);
        RefreshModeDisplay();
        UpdateBadgeVisibility();
        RebuildTrayMenu();
    }

    private static WindowIcon LoadIcon(string state) =>
        new(AssetLoader.Open(new Uri($"avares://SonicScript/Assets/tray-{state}.png")));

    private void SetTrayState(string state)
    {
        _trayState = state;
        RefreshModeDisplay();
    }

    private void RebuildTrayMenu()
    {
        if (_tray is null) return;

        var menu = new NativeMenu();

        // Moduswahl direkt im Tray
        var current = Settings.CurrentEntry;
        var modeMenu = new NativeMenuItem($"{Strings.Get("tray.mode")}: {current.Symbol} {current.Name}") { Menu = new NativeMenu() };
        foreach (var e in Settings.BuildCycle())
        {
            var id = e.Id;
            var item = new NativeMenuItem($"{e.Symbol}  {e.Name}")
            {
                ToggleType = NativeMenuItemToggleType.Radio,
                IsChecked = e.Id == current.Id,
            };
            item.Click += (_, _) => SelectMode(id);
            modeMenu.Menu.Add(item);
        }
        menu.Add(modeMenu);
        menu.Add(new NativeMenuItemSeparator());

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

        var checkUpdates = new NativeMenuItem(Strings.Get("tray.check_updates"));
        checkUpdates.Click += async (_, _) => await CheckForUpdatesAsync(manual: true);

        var history = new NativeMenuItem(Strings.Get("tray.history"));
        history.Click += (_, _) => OpenSettings("history");
        menu.Add(history);

        var stats = new NativeMenuItem(Strings.Get("tray.stats"));
        stats.Click += (_, _) => OpenSettings("stats");
        menu.Add(stats);

        if (_update is { } upd)
        {
            var update = new NativeMenuItem(Strings.Format("tray.update", upd.Tag));
            update.Click += async (_, _) => await InstallUpdateAsync(upd);
            menu.Add(update);
        }
        else
        {
            menu.Add(checkUpdates);
        }

        var settings = new NativeMenuItem(Strings.Get("tray.settings"));
        settings.Click += (_, _) => ShowMainWindow();
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

    public static readonly TimeSpan UpdateInterval = TimeSpan.FromHours(2);

    /// <summary>Kurz nach dem Start und danach alle 2 Stunden nach neuen Versionen suchen (wenn eingeschaltet).</summary>
    private async Task UpdateLoopAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(15));
        while (!_disposing)
        {
            if (Settings.CheckForUpdates) await CheckForUpdatesAsync(manual: false);
            await Task.Delay(UpdateInterval);
        }
    }

    /// <param name="manual">Vom Nutzer ausgelöst: auch "aktuell" und Fehler melden.</param>
    private async Task CheckForUpdatesAsync(bool manual)
    {
        try
        {
            var found = await new UpdateChecker(Http).GetNewerReleaseAsync(CurrentVersion, CancellationToken.None);
            var isNew = found is not null && found.Tag != _update?.Tag;
            _update = found;
            RebuildTrayMenu();
            if (found is not null && (isNew || manual))
                Toast.Show(Strings.Format("toast.update_available", found.Tag));
            else if (found is null && manual)
                Toast.Show(Strings.Format("toast.up_to_date", CurrentVersion.ToString(3)));
        }
        catch (Exception ex)
        {
            Log.Error("Update check", ex);
            if (manual) Toast.Show(Strings.Format("update.failed", Short(ex.Message)), isError: true);
        }
    }

    private bool _updating;

    /// <summary>Neue Version laden, Prüfsumme checken, .exe austauschen und neu starten.</summary>
    private async Task InstallUpdateAsync(ReleaseInfo release)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            Toast.Show(Strings.Format("update.downloading", release.Tag));
            var result = await new Updater(Http).InstallAsync(release, CancellationToken.None);
            switch (result)
            {
                case UpdateResult.Ready:
                    _lifetime.Shutdown();
                    break;
                case UpdateResult.NoWriteAccess:
                    Toast.Show(Strings.Get("update.no_write"), isError: true);
                    OpenUrl(release.HtmlUrl);
                    break;
                case UpdateResult.ChecksumMismatch:
                    Toast.Show(Strings.Get("update.checksum"), isError: true);
                    break;
                default:
                    OpenUrl(release.HtmlUrl);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Update", ex);
            Toast.Show(Strings.Format("update.failed", Short(ex.Message)), isError: true);
        }
        finally
        {
            _updating = false;
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
        _disposing = true;
        _tray?.Dispose();
        _badge?.Close();
        _osd?.Close();
        Http.Dispose();
    }
}
