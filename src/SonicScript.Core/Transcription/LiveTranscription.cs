using SonicScript.Core.Audio;
using SonicScript.Core.Text;

namespace SonicScript.Core.Transcription;

public sealed class LiveTranscriptionOptions
{
    /// <summary>Ab dieser Länge wird an der nächsten Pause ein Stück abgeschlossen und erkannt.</summary>
    public TimeSpan ChunkTarget { get; init; } = TimeSpan.FromSeconds(20);
    /// <summary>Spätestens hier wird geschnitten (an der leisesten Stelle), auch ohne echte Pause.</summary>
    public TimeSpan ChunkMax { get; init; } = TimeSpan.FromSeconds(35);
    /// <summary>Mindestlänge einer Pause, an der geschnitten werden darf.</summary>
    public TimeSpan MinPause { get; init; } = TimeSpan.FromMilliseconds(300);
    /// <summary>Live-Vorschau des noch offenen Stücks (kostet Rechenzeit; nur für lokale Erkennung sinnvoll).</summary>
    public bool Preview { get; init; }
    /// <summary>Neue Vorschau frühestens nach so viel neuem Audio.</summary>
    public TimeSpan PreviewInterval { get; init; } = TimeSpan.FromSeconds(1.5);
    public bool TrimSilence { get; init; } = true;
    public string? Language { get; init; }
    public string? Prompt { get; init; }
    public required string WorkDirectory { get; init; }
    /// <summary>Wie oft der Hintergrund-Takt nachsieht (Tests: kürzer).</summary>
    public TimeSpan Tick { get; init; } = TimeSpan.FromMilliseconds(200);
}

/// <summary>
/// Erkennt schon während der Aufnahme: Lange Diktate werden an Sprechpausen in Stücke geteilt und nacheinander
/// erkannt, sodass am Ende nur noch das letzte Stück fehlt. Optional zeigt eine Vorschau den bisher erkannten Text.
/// Alle Aufrufe an den Erkenner laufen nacheinander (lokales Whisper ist nicht parallel nutzbar).
/// </summary>
public sealed class LiveTranscription : IDisposable
{
    private const int FrameMs = 20;
    private readonly ITranscriber _transcriber;
    private readonly int _rate;
    private readonly LiveTranscriptionOptions _o;
    private readonly object _gate = new();
    private readonly List<short> _samples = [];
    private readonly List<string> _committed = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationTokenSource _previewCts = new();
    private readonly Task _worker;
    private int _committedUpTo;
    private int _previewedUpTo;
    private string? _language;
    private Exception? _error;
    private int _chunkNo;

    /// <summary>Bisher erkannter Text (abgeschlossene Stücke + Vorschau). Kommt auf einem Hintergrund-Thread.</summary>
    public event Action<string>? PreviewChanged;

    /// <summary>Anzahl abgeschlossener Stücke (für Tests/Log).</summary>
    public int CommittedChunks { get { lock (_gate) return _committed.Count; } }

    public LiveTranscription(ITranscriber transcriber, int sampleRate, LiveTranscriptionOptions options)
    {
        _transcriber = transcriber;
        _rate = sampleRate;
        _o = options;
        Directory.CreateDirectory(options.WorkDirectory);
        _worker = Task.Run(WorkAsync);
    }

    /// <summary>Neue Samples (vom Aufnahme-Thread).</summary>
    public void Append(ReadOnlySpan<short> samples)
    {
        lock (_gate)
        {
            foreach (var s in samples) _samples.Add(s);
        }
    }

    private int Samples(TimeSpan t) => (int)(t.TotalSeconds * _rate);

    private async Task WorkAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try { await Task.Delay(_o.Tick, _stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            if (_error is not null) continue;

            short[] tail;
            int start;
            var chunking = false;
            lock (_gate)
            {
                start = _committedUpTo;
                tail = _samples.GetRange(start, _samples.Count - start).ToArray();
            }

            try
            {
                if (tail.Length >= Samples(_o.ChunkTarget) && FindCut(tail) is { } cut)
                {
                    chunking = true;
                    var text = await TranscribeAsync(tail.AsSpan(0, cut).ToArray(), _stop.Token).ConfigureAwait(false);
                    lock (_gate)
                    {
                        if (text.Length > 0) _committed.Add(text);
                        _committedUpTo = start + cut;
                        _previewedUpTo = _committedUpTo;
                    }
                    RaisePreview(null);
                }
                else if (_o.Preview && tail.Length >= Samples(TimeSpan.FromSeconds(0.8)) &&
                         start + tail.Length - Math.Max(_previewedUpTo, start) >= Samples(_o.PreviewInterval))
                {
                    _previewedUpTo = start + tail.Length;
                    var text = await TranscribeAsync(tail, _previewCts.Token).ConfigureAwait(false);
                    bool stale;
                    lock (_gate) stale = _committedUpTo != start;
                    if (!stale) RaisePreview(text);
                }
            }
            catch (OperationCanceledException)
            {
                // Vorschau abgebrochen (Aufnahme fertig) oder Ende
            }
            catch (Exception ex)
            {
                // Fehler bei einem Stück: am Ende wird alles auf einmal (wie früher) behandelt.
                if (chunking) _error = ex;
            }
        }
    }

    private void RaisePreview(string? tailText)
    {
        string text;
        lock (_gate) text = Join(_committed.Append(tailText ?? ""));
        if (text.Length > 0) PreviewChanged?.Invoke(text);
    }

    private static string Join(IEnumerable<string> parts) =>
        string.Join(" ", parts.Select(p => p.Trim()).Where(p => p.Length > 0));

    /// <summary>
    /// Schnittstelle im offenen Stück: Mitte der längsten Pause zwischen 60 % des Ziels und dem Ende,
    /// ab der Maximallänge notfalls die leiseste Stelle. null = noch warten.
    /// </summary>
    internal int? FindCut(short[] tail)
    {
        var frameLen = _rate * FrameMs / 1000;
        var frames = tail.Length / frameLen;
        var energy = new double[frames];
        for (var f = 0; f < frames; f++)
        {
            double sum = 0;
            for (var i = 0; i < frameLen; i++)
            {
                var v = tail[f * frameLen + i] / 32768.0;
                sum += v * v;
            }
            energy[f] = Math.Sqrt(sum / frameLen);
        }
        var speech = SilenceTrimmer.DetectSpeech(energy);
        var from = (int)(_o.ChunkTarget.TotalMilliseconds * 0.6 / FrameMs);
        var maxFrames = (int)(_o.ChunkMax.TotalMilliseconds / FrameMs);
        var to = Math.Min(frames, maxFrames);
        var minPause = (int)(_o.MinPause.TotalMilliseconds / FrameMs);

        int bestStart = -1, bestLen = 0;
        for (var f = from; f < to;)
        {
            if (speech[f]) { f++; continue; }
            var s = f;
            while (f < to && !speech[f]) f++;
            // Pause, die bis ans Ende reicht, ist evtl. noch nicht vorbei – trotzdem nutzbar, wenn lang genug.
            if (f - s > bestLen) { bestLen = f - s; bestStart = s; }
        }
        if (bestLen >= minPause) return (bestStart + bestLen / 2) * frameLen;
        if (frames < maxFrames) return null;

        var quietest = from;
        for (var f = from; f < maxFrames; f++) if (energy[f] < energy[quietest]) quietest = f;
        return quietest * frameLen;
    }

    private async Task<string> TranscribeAsync(short[] samples, CancellationToken ct)
    {
        var path = Path.Combine(_o.WorkDirectory, $"live-{Guid.NewGuid():N}-{Interlocked.Increment(ref _chunkNo)}.wav");
        PcmWav.Write(path, samples, _rate);
        TrimResult? trim = null;
        try
        {
            trim = _o.TrimSilence ? SilenceTrimmer.Trim(path) : null;
            if (trim is { HasSpeech: false }) return "";
            var r = await _transcriber.TranscribeAsync(new TranscriptionRequest(trim?.Path ?? path, _o.Language, _o.Prompt), ct).ConfigureAwait(false);
            _language ??= r.Language;
            return HallucinationFilter.Clean(r.Text);
        }
        finally
        {
            TryDelete(path);
            if (trim is not null && trim.Path != path) TryDelete(trim.Path);
        }
    }

    /// <summary>
    /// Aufnahme ist fertig: Vorschau abbrechen, Rest erkennen, alles zusammensetzen.
    /// null = keine Sprache. Wirft, wenn ein Stück nicht erkannt werden konnte.
    /// </summary>
    public async Task<TranscriptionResult?> FinishAsync(CancellationToken ct)
    {
        _previewCts.Cancel();
        _stop.Cancel();
        try { await _worker.ConfigureAwait(false); } catch (OperationCanceledException) { }
        if (_error is not null) throw _error;

        short[] tail;
        lock (_gate) tail = _samples.GetRange(_committedUpTo, _samples.Count - _committedUpTo).ToArray();
        var last = tail.Length >= Samples(TimeSpan.FromMilliseconds(200)) ? await TranscribeAsync(tail, ct).ConfigureAwait(false) : "";
        string text;
        lock (_gate) text = Join(_committed.Append(last));
        return text.Length == 0 ? null : new TranscriptionResult(text, _language);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _previewCts.Cancel();
        _stop.Cancel();
    }
}
