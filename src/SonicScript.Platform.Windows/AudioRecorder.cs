using NAudio.Wave;

namespace SonicScript.Platform.Windows;

public sealed record MicrophoneInfo(int Index, string Name);

/// <summary>Aufnahme 16 kHz / mono / 16 bit als WAV (das Format, das Whisper erwartet).</summary>
public sealed class AudioRecorder : IDisposable
{
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private TaskCompletionSource<string?>? _stopped;
    private string? _path;
    private bool _discard;

    /// <summary>Pegel 0..1 (RMS, für den Indikator). Kommt auf einem Hintergrund-Thread.</summary>
    public event Action<float>? Level;

    /// <summary>Rohdaten (16 kHz, mono) für die Live-Erkennung. Kommt auf einem Hintergrund-Thread.</summary>
    public event Action<short[]>? Samples;

    public bool IsRecording => _waveIn is not null;

    public static IReadOnlyList<MicrophoneInfo> Microphones()
    {
        var list = new List<MicrophoneInfo>();
        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
            list.Add(new MicrophoneInfo(i, WaveInEvent.GetCapabilities(i).ProductName));
        return list;
    }

    /// <summary>
    /// Gerätenummer zum gespeicherten Namen suchen (Nummern verschieben sich beim An-/Abstecken).
    /// Nicht gefunden → Windows-Standardgerät (-1).
    /// </summary>
    public static int Resolve(int index, string? name)
    {
        try
        {
            var mics = Microphones();
            if (!string.IsNullOrWhiteSpace(name))
                return mics.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))?.Index ?? -1;
            return index >= 0 && index < mics.Count ? index : -1;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    /// <summary>Fehler, mit dem die letzte Aufnahme abbrach (z. B. Mikrofon abgezogen); das Aufgenommene bleibt.</summary>
    public Exception? LastError { get; private set; }

    public void Start(string path, int deviceIndex)
    {
        if (_waveIn is not null) throw new InvalidOperationException("already_recording");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _path = path;
        _discard = false;
        LastError = null;
        _stopped = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waveIn = new WaveInEvent
        {
            DeviceNumber = deviceIndex >= 0 && deviceIndex < WaveInEvent.DeviceCount ? deviceIndex : -1,
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 50,
        };
        try
        {
            _writer = new WaveFileWriter(path, waveIn.WaveFormat);
            waveIn.DataAvailable += OnData;
            waveIn.RecordingStopped += OnStopped;
            waveIn.StartRecording();
            _waveIn = waveIn;
        }
        catch
        {
            waveIn.Dispose();
            _writer?.Dispose();
            _writer = null;
            TryDelete(path);
            throw;
        }
    }

    /// <summary>Stoppt und liefert den Pfad der fertigen WAV-Datei.</summary>
    public Task<string?> StopAsync()
    {
        if (_waveIn is null || _stopped is null) return Task.FromResult<string?>(null);
        _waveIn.StopRecording();
        return _stopped.Task;
    }

    /// <summary>Stoppt und verwirft die Aufnahme.</summary>
    public async Task CancelAsync()
    {
        _discard = true;
        await StopAsync().ConfigureAwait(false);
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        if (Samples is { } handler && e.BytesRecorded >= 2)
        {
            var chunk = new short[e.BytesRecorded / 2];
            Buffer.BlockCopy(e.Buffer, 0, chunk, 0, chunk.Length * 2);
            handler(chunk);
        }
        if (Level is null || e.BytesRecorded < 2) return;
        double sum = 0;
        var n = e.BytesRecorded / 2;
        for (var i = 0; i < n; i++)
        {
            var s = BitConverter.ToInt16(e.Buffer, i * 2) / 32768.0;
            sum += s * s;
        }
        Level?.Invoke((float)Math.Min(1, Math.Sqrt(sum / n) * 4));
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        _writer?.Dispose();
        _writer = null;
        if (_waveIn is not null)
        {
            _waveIn.DataAvailable -= OnData;
            _waveIn.RecordingStopped -= OnStopped;
            _waveIn.Dispose();
            _waveIn = null;
        }
        var path = _path;
        LastError = e.Exception;
        // Gerät weg (abgezogen, Treiber neu gestartet): Bis dahin Aufgenommenes trotzdem verarbeiten.
        var hasAudio = path is not null && File.Exists(path) && new FileInfo(path).Length > 44 + 16000;
        if (_discard || (e.Exception is not null && !hasAudio))
        {
            if (path is not null) TryDelete(path);
            path = null;
        }
        if (e.Exception is not null && path is null) _stopped?.TrySetException(e.Exception);
        else _stopped?.TrySetResult(path);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        if (_waveIn is not null)
        {
            _discard = true;
            _waveIn.StopRecording();
        }
    }
}
