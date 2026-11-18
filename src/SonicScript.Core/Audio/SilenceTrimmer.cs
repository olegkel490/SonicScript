using System.Buffers.Binary;

namespace SonicScript.Core.Audio;

public sealed record TrimResult(string Path, TimeSpan Original, TimeSpan Trimmed, bool HasSpeech)
{
    public bool Changed => Trimmed < Original;
}

/// <summary>
/// Schneidet Stille am Anfang und Ende ab und kürzt lange Pausen in der Mitte. Das beschleunigt die lokale Erkennung
/// (weniger Audio) und verringert erfundene Sätze, die Whisper in Stille hineinhalluziniert.
/// Nur für 16-bit-PCM-WAV (wie SonicScript aufnimmt); andere Dateien bleiben unverändert.
/// </summary>
public static class SilenceTrimmer
{
    private const int FrameMs = 20;
    /// <summary>Puffer vor/nach Sprache, damit Silbenanfänge und -enden nicht abgeschnitten werden.</summary>
    public static readonly TimeSpan Padding = TimeSpan.FromMilliseconds(250);
    /// <summary>Pausen länger als das werden auf <see cref="KeptPause"/> gekürzt.</summary>
    public static readonly TimeSpan LongPause = TimeSpan.FromMilliseconds(1200);
    public static readonly TimeSpan KeptPause = TimeSpan.FromMilliseconds(500);
    /// <summary>Weniger Sprache als das gilt als "nichts gesagt".</summary>
    public static readonly TimeSpan MinSpeech = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Schreibt eine gekürzte Kopie neben die Datei ("…-trim.wav"), falls sich das lohnt; sonst ist
    /// <see cref="TrimResult.Path"/> die Originaldatei. Bei Fehlern wird nie geworfen.
    /// </summary>
    public static TrimResult Trim(string path)
    {
        try
        {
            var wav = PcmWav.Read(path);
            if (wav is null) return Unchanged(path);
            var (samples, rate) = wav.Value;
            var original = TimeSpan.FromSeconds((double)samples.Length / rate);

            var frameLen = rate * FrameMs / 1000;
            var frames = samples.Length / frameLen;
            if (frames < 3) return new TrimResult(path, original, original, true);

            var energy = new double[frames];
            for (var f = 0; f < frames; f++)
            {
                double sum = 0;
                for (var i = 0; i < frameLen; i++)
                {
                    var v = samples[f * frameLen + i] / 32768.0;
                    sum += v * v;
                }
                energy[f] = Math.Sqrt(sum / frameLen);
            }

            var speech = DetectSpeech(energy);
            var speechFrames = speech.Count(s => s);
            if (speechFrames * FrameMs < MinSpeech.TotalMilliseconds)
                return new TrimResult(path, original, TimeSpan.Zero, false);

            // Sprache + Puffer behalten, lange Pausen kürzen.
            var pad = (int)(Padding.TotalMilliseconds / FrameMs);
            var keep = new bool[frames];
            for (var f = 0; f < frames; f++)
            {
                if (!speech[f]) continue;
                for (var k = Math.Max(0, f - pad); k <= Math.Min(frames - 1, f + pad); k++) keep[k] = true;
            }
            var maxPause = (int)(LongPause.TotalMilliseconds / FrameMs);
            var keptPause = (int)(KeptPause.TotalMilliseconds / FrameMs);
            for (var f = 0; f < frames;)
            {
                if (keep[f]) { f++; continue; }
                var start = f;
                while (f < frames && !keep[f]) f++;
                var len = f - start;
                var inner = start > 0 && f < frames;
                if (inner && len <= maxPause)
                {
                    for (var k = start; k < f; k++) keep[k] = true;      // normale Pause behalten
                }
                else if (inner)
                {
                    for (var k = start; k < start + keptPause; k++) keep[k] = true; // lange Pause kürzen
                }
            }

            var kept = keep.Count(k => k);
            if (kept >= frames - 2) return new TrimResult(path, original, original, true); // lohnt nicht

            var outSamples = new short[kept * frameLen];
            var o = 0;
            for (var f = 0; f < frames; f++)
            {
                if (!keep[f]) continue;
                Array.Copy(samples, f * frameLen, outSamples, o, frameLen);
                o += frameLen;
            }
            var outPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!,
                System.IO.Path.GetFileNameWithoutExtension(path) + "-trim.wav");
            PcmWav.Write(outPath, outSamples, rate);
            return new TrimResult(outPath, original, TimeSpan.FromSeconds((double)outSamples.Length / rate), true);
        }
        catch (Exception)
        {
            return Unchanged(path);
        }
    }

    private static TrimResult Unchanged(string path)
    {
        TimeSpan d;
        try { d = WavFile.GetDuration(path) ?? TimeSpan.Zero; }
        catch (Exception) { d = TimeSpan.Zero; }
        return new TrimResult(path, d, d, true);
    }

    /// <summary>
    /// Sprache = Energie deutlich über dem Grundrauschen. Grundrauschen = 10-%-Quantil der Rahmen-Energie;
    /// Schwelle mindestens 0,006 (sehr leise Aufnahmen) und höchstens 0,05.
    /// </summary>
    internal static bool[] DetectSpeech(double[] energy)
    {
        var sorted = energy.OrderBy(e => e).ToArray();
        var floor = sorted[(int)(sorted.Length * 0.1)];
        var threshold = Math.Clamp(floor * 3.0, 0.006, 0.05);
        var speech = new bool[energy.Length];
        for (var i = 0; i < energy.Length; i++) speech[i] = energy[i] > threshold;

        // Einzelne Ausreißer-Rahmen (Klick, Knacken) ignorieren: mindestens 2 aufeinanderfolgende.
        var clean = new bool[energy.Length];
        for (var i = 0; i < energy.Length; i++)
            clean[i] = speech[i] && ((i > 0 && speech[i - 1]) || (i + 1 < energy.Length && speech[i + 1]));
        return clean;
    }
}

/// <summary>Lesen/Schreiben von 16-bit-PCM-Mono-WAV.</summary>
public static class PcmWav
{
    public static (short[] Samples, int Rate)? Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 44 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8)) return null;
        int pos = 12, rate = 0, channels = 0, bits = 0, format = 0;
        while (pos + 8 <= bytes.Length)
        {
            var id = bytes.AsSpan(pos, 4);
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos + 4, 4));
            var body = pos + 8;
            if (id.SequenceEqual("fmt "u8) && size >= 16)
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body + 2, 2));
                rate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(body + 4, 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body + 14, 2));
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (format != 1 || channels != 1 || bits != 16 || rate <= 0) return null;
                var len = Math.Min(size is 0 or < 0 ? bytes.Length - body : size, bytes.Length - body) / 2;
                var samples = new short[len];
                for (var i = 0; i < len; i++) samples[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(body + i * 2, 2));
                return (samples, rate);
            }
            pos = body + size + (size & 1);
        }
        return null;
    }

    public static void Write(string path, short[] samples, int rate)
    {
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        var dataBytes = samples.Length * 2;
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(dataBytes);
        foreach (var s in samples) w.Write(s);
    }
}
