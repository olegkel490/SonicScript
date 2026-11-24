using Avalonia.Threading;
using SonicScript.App.Views;
using SonicScript.Core.Localization;
using SonicScript.Core.Modes;
using SonicScript.Core.Pipeline;
using SonicScript.Core.Reminders;
using SonicScript.Core.Settings;
using SonicScript.Core.Text;
using SonicScript.Core.Transcription;
using SonicScript.Platform.Windows;

namespace SonicScript.App.Services;

/// <summary>Diktat fortsetzen und Erinnerungen aus Notizen.</summary>
public sealed partial class AppController
{
    // ── Diktat fortsetzen ──────────────────────────────────────────────────────

    /// <summary>Modus des letzten Diktats (sonst der gewählte).</summary>
    private ModeDefinition ContinueMode(AppSettings s) =>
        s.FindMode(_lastInsert?.ModeId) ?? (_last is { } last && s.FindMode(last.Mode.Id) is { } m ? m : null) ??
        s.CurrentEntry.Mode ?? s.DefaultMode;

    /// <summary>
    /// Weitersprechen: Im KI-Modus wird das bisherige Diktat zusammen mit dem neuen Teil neu geglättet und ersetzt den
    /// zuletzt eingefügten Text; wortgetreu wird einfach angehängt. Ohne passendes letztes Diktat (anderes Fenster,
    /// zu lange her) wie ein normales Diktat.
    /// </summary>
    private async Task ContinueAsync(Session s, DictationPipeline pipeline, string path, FocusContext? context, bool live, TranscriptionResult? transcript)
    {
        var tr = live ? transcript : await Task.Run(() => pipeline.TranscribeFileAsync(path, s.Settings, s.Cts.Token));
        if (tr is null || string.IsNullOrWhiteSpace(tr.Text))
        {
            Toast.Show(Strings.Get("toast.nothing_heard"));
            return;
        }

        var li = _lastInsert;
        var usable = li is { Verbatim.Length: > 0 } && DateTime.UtcNow - li.At < UndoWindow &&
                     li.Target.Handle == s.Target.Handle && ForegroundWindow.Current == li.Target.Handle;

        if (!usable || !s.Mode.NeedsLanguageModel)
        {
            if (!usable) Toast.Show(Strings.Get("toast.continue_new"));
            var single = await Task.Run(() => pipeline.RunTranscriptAsync(tr, s.Mode, s.Settings, s.Cts.Token));
            await DeliverAsync(s, single, context);
            if (usable && _lastInsert is { } now && now != li)
                _lastInsert = now with { Verbatim = li!.Verbatim + " " + single.Verbatim, Final = li.Final + " " + single.Final, Before = li.Before };
            return;
        }

        var combined = new TranscriptionResult(li!.Verbatim + " " + tr.Text.Trim(), tr.Language ?? s.Settings.Transcription.Language, tr.Fallback);
        var result = await Task.Run(() => pipeline.RunTranscriptAsync(combined, s.Mode, s.Settings, s.Cts.Token));
        if (result.IsEmpty) return;

        if (!await TextInserter.UndoAsync(li.Target))
        {
            await ClipboardService.SetTextAsync(result.Final);
            Toast.Show(Strings.Get("toast.continue_clipboard"), isError: true);
            Remember(s, result, tr.Text);
            return;
        }
        await Task.Delay(120);
        var text = s.Settings.Insertion.FitToContext ? InsertionFitter.Fit(result.Final, li.Before) : result.Final;
        var outcome = await _inserter.InsertAsync(text, InsertionMethod.Paste, li.Target, s.Settings.Insertion);
        ReportOutcome(outcome, InsertionMethod.Paste);
        _lastInsert = outcome == InsertOutcome.Inserted
            ? new LastInsert(li.Target, result.Final, result.Verbatim, li.Before, DateTime.UtcNow, false, result.Mode.Id)
            : null;
        ShowWarning(result);
        Remember(s, result, tr.Text);
    }

    // ── Erinnerungen ───────────────────────────────────────────────────────────

    private DispatcherTimer? _reminderTimer;
    private string? _reminderShowing;

    private void StartReminderTimer()
    {
        _reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _reminderTimer.Tick += (_, _) => ShowDueReminder();
        _reminderTimer.Start();
        DispatcherTimer.RunOnce(ShowDueReminder, TimeSpan.FromSeconds(8)); // verpasste nach dem Start nachholen
    }

    private void ShowDueReminder()
    {
        try
        {
            if (_reminderShowing is not null || _recording is not null) return;
            var due = Reminders.DueAt(DateTime.Now);
            if (due.Count == 0) return;
            var r = due[0];
            _reminderShowing = r.Id;
            Log.Info($"Reminder due ({r.Due:yyyy-MM-dd HH:mm})");
            if (Settings.PlaySounds) Sounds.Play(Cue.Start);
            var late = DateTime.Now - r.Due > TimeSpan.FromMinutes(15) ? " " + Strings.Format("reminder.late", r.Due.ToString("ddd HH:mm", System.Globalization.CultureInfo.CurrentCulture)) : "";
            Toast.Show("⏰ " + r.Text + late + (due.Count > 1 ? " " + Strings.Format("reminder.more", due.Count - 1) : ""), false,
                Strings.Get("reminder.done"), () => { Reminders.Complete(r.Id); _reminderShowing = null; ShowNextSoon(); },
                Strings.Get("reminder.snooze"), () => { Reminders.Snooze(r.Id, DateTime.Now.AddMinutes(10)); _reminderShowing = null; ShowNextSoon(); },
                TimeSpan.FromMinutes(2),
                () => { Reminders.Ignored(r.Id, DateTime.Now); _reminderShowing = null; });
        }
        catch (Exception ex)
        {
            _reminderShowing = null;
            Log.Error("Reminder", ex);
        }
    }

    private void ShowNextSoon() => DispatcherTimer.RunOnce(ShowDueReminder, TimeSpan.FromSeconds(2));
}
