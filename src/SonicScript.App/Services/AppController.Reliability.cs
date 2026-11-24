using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Avalonia.Threading;
using SonicScript.App.Views;
using SonicScript.Core.Audio;
using SonicScript.Core.Localization;
using SonicScript.Core.Net;
using SonicScript.Core.Settings;
using SonicScript.Core.Transcription;
using SonicScript.Core.Updates;
using SonicScript.LocalWhisper;
using SonicScript.Platform.Windows;

namespace SonicScript.App.Services;

public enum DiagnosticStatus { Ok, Warning, Error, Info }

public sealed record DiagnosticResult(string Name, DiagnosticStatus Status, string Detail);

/// <summary>Zuverlässigkeit: Update-Überwachung mit Rückfall, Umzug aus Sync-Ordnern, Diagnose und Fehlerbericht.</summary>
public sealed partial class AppController
{
    public static readonly TimeSpan UpdateHealthyAfter = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan UpdateTimeout = TimeSpan.FromSeconds(90);

    // ── Update mit Rückfall ────────────────────────────────────────────────────

    /// <summary>
    /// Alte Instanz nach dem Austausch der .exe: unsichtbar weiterlaufen, bis die neue Version meldet, dass sie
    /// stabil läuft. Stürzt sie ab oder hängt, die alte Version zurückholen und neu starten.
    /// </summary>
    private async Task SuperviseUpdateAsync(Updater updater, ReleaseInfo release)
    {
        ExitReason = "update to " + release.Tag;
        _hook.Dispose();
        CloseBadge();
        if (_tray is not null) _tray.IsVisible = false;
        Program.ReleaseSingleInstance();

        var healthy = await Task.Run(() =>
        {
            var started = updater.Started;
            var deadline = DateTime.UtcNow + UpdateTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (updater.Healthy?.WaitOne(500) == true) return true;
                if (started is { HasExited: true }) return false;
            }
            return false;
        });

        if (!healthy)
        {
            Log.Info($"Update to {release.Tag} did not start properly – rolling back");
            ExitReason = $"update to {release.Tag} failed, rolled back";
            if (!await Task.Run(() => Updater.RollBack(updater.Started))) Log.Info("Rollback not possible");
        }
        else
        {
            Log.Info($"Update to {release.Tag} confirmed healthy");
        }
        updater.Healthy?.Dispose();
        _lifetime.Shutdown();
    }

    /// <summary>Neue Version: nach kurzer stabiler Laufzeit der alten Instanz Bescheid geben und deren Datei entfernen.</summary>
    private async Task ConfirmUpdateHealthyAsync()
    {
        await Task.Delay(UpdateHealthyAfter);
        Updater.SignalHealthy();
        await Task.Delay(TimeSpan.FromSeconds(3));
        await Task.Run(Updater.CleanupOldVersion);
    }

    // ── Umzug aus OneDrive ─────────────────────────────────────────────────────

    private void OfferMoveIfSynced()
    {
        var loc = AppLocation.Current;
        if (!loc.CanMove || Settings.MoveOfferDismissed) return;
        Toast.Show(Strings.Get("toast.move_offer"), false,
            Strings.Get("settings.location.move"), MoveToRecommendedLocation,
            Strings.Get("toast.learn_ignore"), () => { Settings.MoveOfferDismissed = true; SaveQuietly(); });
    }

    /// <summary>.exe nach %LOCALAPPDATA%\Programs\SonicScript kopieren, Verknüpfungen anlegen, dort neu starten.</summary>
    public void MoveToRecommendedLocation()
    {
        var loc = AppLocation.Current;
        if (!loc.CanMove) return;
        var target = AppLocation.RecommendedPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(loc.ExePath, target, overwrite: true);
            try
            {
                Shortcuts.Create(Shortcuts.StartMenuPath, target, "SonicScript");
                Shortcuts.Create(Shortcuts.DesktopPath, target, "SonicScript");
            }
            catch (Exception ex) { Log.Error("Shortcuts", ex); }

            ExitReason = "moved to " + target;
            Program.ReleaseSingleInstance();
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target)!, ArgumentList = { AppLocation.MovedArg, loc.ExePath } });
            Log.Info($"Moved from {loc.ExePath} to {target}");
            _lifetime.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Error("Move", ex);
            Toast.Show(Strings.Format("settings.transfer.failed", Short(ex.Message)), isError: true);
        }
    }

    /// <summary>Neue Instanz am neuen Ort: alte Datei entfernen, Autostart umstellen.</summary>
    private void FinishMove(string oldPath)
    {
        Toast.Show(Strings.Format("toast.moved", Path.GetDirectoryName(Environment.ProcessPath ?? "")!));
        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 40 && File.Exists(oldPath); i++)
            {
                try { File.Delete(oldPath); }
                catch (IOException) { await Task.Delay(500); }
                catch (UnauthorizedAccessException) { await Task.Delay(500); }
            }
            Log.Info(File.Exists(oldPath) ? "Old copy could not be deleted: " + oldPath : "Old copy deleted: " + oldPath);
        });
    }

    // ── Diagnose ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<DiagnosticResult>> RunDiagnosticsAsync()
    {
        var s = Settings;
        var results = new List<DiagnosticResult>();
        void Add(string key, DiagnosticStatus st, string detail) => results.Add(new DiagnosticResult(Strings.Get("diag." + key), st, detail));
        static string Ms(Stopwatch sw) => $"{sw.ElapsedMilliseconds} ms";

        // Version / Speicherort
        var loc = AppLocation.Current;
        Add("version", DiagnosticStatus.Info, $"{CurrentVersion.ToString(3)} · {Environment.OSVersion.VersionString}");
        Add("location", loc.IsSynced ? DiagnosticStatus.Warning : DiagnosticStatus.Ok,
            loc.IsSynced ? Strings.Get("diag.location_synced") : loc.ExePath);

        // Tastatur
        var (installed, since) = HookStatus;
        Add("hook", installed ? DiagnosticStatus.Ok : DiagnosticStatus.Error,
            installed ? Strings.Format("diag.hook_ok", (int)since.TotalSeconds) : Strings.Get("diag.hook_missing"));
        if (!installed)
        {
            try { _hook.Reinstall(); } catch (Exception ex) { Log.Error("Hook reinstall", ex); }
        }

        // Mikrofon: 1,5 s aufnehmen und Pegel messen
        if (_recording is null && _processing is null)
        {
            var device = AudioRecorder.Resolve(s.Transcription.MicrophoneDevice, s.Transcription.MicrophoneName);
            var micName = device < 0 ? Strings.Get("settings.microphone.default") : s.Transcription.MicrophoneName;
            var path = Path.Combine(AppPaths.Recordings, $"diag-{Guid.NewGuid():N}.wav");
            try
            {
                using var rec = new AudioRecorder();
                float max = 0;
                rec.Level += l => max = Math.Max(max, l);
                rec.Start(path, device);
                await Task.Delay(1500);
                await rec.StopAsync();
                Add("mic", max > 0.02 ? DiagnosticStatus.Ok : DiagnosticStatus.Warning,
                    $"{micName} · " + (max > 0.02 ? Strings.Format("diag.mic_level", (int)(max * 100)) : Strings.Get("diag.mic_silent")));
            }
            catch (Exception ex)
            {
                Add("mic", DiagnosticStatus.Error, $"{micName} · {Short(ex.Message)}");
            }
            finally
            {
                TryDelete(path);
            }
        }

        // Spracherkennung mit 1 s Testton
        var transcriber = EnsureTranscriber();
        var engine = s.Transcription.Engine == TranscriptionEngine.Local
            ? $"{Strings.Get("diag.local")} ({s.Transcription.LocalModelId}{(LocalWhisperTranscriber.IsGpuActive ? ", GPU" : "")})"
            : $"{Strings.Get("diag.cloud")} ({s.Transcription.Cloud.Model})";
        if (transcriber is null)
        {
            Add("transcription", DiagnosticStatus.Error, engine + " · " + Strings.Get("toast.no_model"));
        }
        else
        {
            var wav = Path.Combine(AppPaths.Recordings, $"diag-{Guid.NewGuid():N}.wav");
            try
            {
                PcmWav.Write(wav, Enumerable.Range(0, 16000).Select(i => (short)(Math.Sin(i * 0.1) * 3000)).ToArray(), 16000);
                var sw = Stopwatch.StartNew();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                await Task.Run(() => transcriber.TranscribeAsync(new TranscriptionRequest(wav, "de", null), cts.Token));
                Add("transcription", DiagnosticStatus.Ok, $"{engine} · {Ms(sw)}");
            }
            catch (Exception ex)
            {
                Add("transcription", DiagnosticStatus.Error, $"{engine} · {Short(ex.Message)}");
            }
            finally
            {
                TryDelete(wav);
            }
        }
        if (s.Transcription.Engine == TranscriptionEngine.Cloud)
        {
            var ok = LocalFallbackTranscriber(s) is not null;
            Add("fallback_local", ok ? DiagnosticStatus.Ok : DiagnosticStatus.Info,
                ok ? Strings.Get("diag.fallback_local_ok") : Strings.Get("diag.fallback_local_none"));
        }

        // Sprachmodell (+ Ersatzmodell)
        async Task TestChat(string key, IChatClientFactory make, string model)
        {
            var client = make();
            if (client is null) { Add(key, DiagnosticStatus.Warning, Strings.Get("toast.mode_not_configured")); return; }
            try
            {
                var sw = Stopwatch.StartNew();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var answer = await Task.Run(() => client.CompleteAsync("Antworte nur mit dem Wort OK.", "Test", 0, cts.Token));
                Add(key, string.IsNullOrWhiteSpace(answer) ? DiagnosticStatus.Warning : DiagnosticStatus.Ok,
                    $"{model} · {Ms(sw)}" + (string.IsNullOrWhiteSpace(answer) ? " · " + Strings.Get("reject.Empty") : ""));
            }
            catch (Exception ex)
            {
                Add(key, DiagnosticStatus.Error, $"{model} · {Short(ex.Message)}");
            }
        }
        await TestChat("llm", () => CreateChatClient(s), s.LanguageModel.Endpoint.Model);
        if (!string.IsNullOrWhiteSpace(s.LanguageModel.FallbackModel))
            await TestChat("llm_fallback", () => CreateChatClient(s, fallback: true), s.LanguageModel.FallbackModel);

        // OpenRouter-Guthaben
        try
        {
            if (await GetOpenRouterUsageAsync(CancellationToken.None) is { } u)
            {
                var low = u.Remaining is { } r && r < 1;
                Add("credit", low ? DiagnosticStatus.Warning : DiagnosticStatus.Ok,
                    Strings.Format("settings.cost.values", Usd(u.Day), Usd(u.Week), Usd(u.Month), Usd(u.Total)) +
                    (u.Remaining is not null ? " · " + Strings.Format("settings.cost.remaining", Usd(u.Remaining)) : ""));
            }
        }
        catch (Exception ex)
        {
            Add("credit", DiagnosticStatus.Error, Short(ex.Message));
        }

        // Updates erreichbar?
        try
        {
            var newer = await new UpdateChecker(Http).GetNewerReleaseAsync(CurrentVersion, CancellationToken.None);
            Add("update", newer is null ? DiagnosticStatus.Ok : DiagnosticStatus.Info,
                newer is null ? Strings.Format("toast.up_to_date", CurrentVersion.ToString(3)) : Strings.Format("toast.update_available", newer.Tag));
        }
        catch (Exception ex)
        {
            Add("update", DiagnosticStatus.Warning, Short(ex.Message));
        }

        Log.Info("Diagnostics: " + string.Join(" | ", results.Select(r => $"{r.Name}={r.Status}")));
        return results;
    }

    private delegate Core.LanguageModel.IChatClient? IChatClientFactory();

    private static string Usd(double? v) => v is null ? "–" : "$" + v.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>ZIP mit Log, Einstellungen (ohne API-Schlüssel) und Diagnose – zum Weitergeben bei Problemen.</summary>
    public async Task WriteSupportReportAsync(string zipPath, IReadOnlyList<DiagnosticResult> diagnostics)
    {
        var info = new StringBuilder()
            .AppendLine($"SonicScript {CurrentVersion.ToString(3)}")
            .AppendLine($"Windows {Environment.OSVersion.VersionString}, {Environment.ProcessorCount} CPU, .NET {Environment.Version}")
            .AppendLine($"Exe: {Environment.ProcessPath}")
            .AppendLine($"Erstellt: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
            .AppendLine();
        foreach (var d in diagnostics) info.AppendLine($"[{d.Status}] {d.Name}: {d.Detail}");
        var settings = SettingsTransfer.Export(Settings, CurrentVersion.ToString(3));

        await Task.Run(() =>
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            void AddText(string name, string text)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write(text);
            }
            AddText("info.txt", info.ToString());
            AddText("einstellungen.json", settings);
            foreach (var log in new[] { AppPaths.LogFile + ".1", AppPaths.LogFile })
            {
                if (!File.Exists(log)) continue;
                // Log ist offen (wir schreiben gerade) → mit Freigabe lesen.
                using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var src = new StreamReader(fs);
                AddText(Path.GetFileName(log), src.ReadToEnd());
            }
        });
        Log.Info("Support report written");
    }
}
