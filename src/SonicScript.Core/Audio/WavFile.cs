using System.Buffers.Binary;

namespace SonicScript.Core.Audio;

/// <summary>Minimaler WAV-Helfer (PCM): Dauer lesen und Testsignal schreiben.</summary>
public static class WavFile
{
    /// <summary>Dauer aus dem Header (fmt- und data-Chunk). Null, wenn keine gültige WAV-Datei.</summary>
    public static TimeSpan? GetDuration(string path)
    {
        using var fs = File.OpenRead(path);
        Span<byte> hdr = stackalloc byte[12];
        if (fs.Read(hdr) != 12 || !hdr[..4].SequenceEqual("RIFF"u8) || !hdr[8..12].SequenceEqual("WAVE"u8)) return null;

        int byteRate = 0;
        Span<byte> chunk = stackalloc byte[8];
        while (fs.Read(chunk) == 8)
        {
            var id = chunk[..4];
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            if (id.SequenceEqual("fmt "u8))
            {
                Span<byte> fmt = stackalloc byte[16];
                if (size < 16 || fs.Read(fmt) != 16) return null;
                byteRate = BinaryPrimitives.ReadInt32LittleEndian(fmt[8..12]);
                fs.Seek(size - 16 + (size & 1), SeekOrigin.Current);
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (byteRate <= 0) return null;
                // Während der Aufnahme kann size noch 0/unbekannt sein → Dateigröße verwenden.
                var dataSize = size is 0 or uint.MaxValue ? fs.Length - fs.Position : Math.Min(size, fs.Length - fs.Position);
                return TimeSpan.FromSeconds((double)dataSize / byteRate);
            }
            else
            {
                fs.Seek(size + (size & 1), SeekOrigin.Current);
            }
        }
        return null;
    }

    /// <summary>16 kHz, mono, 16 bit, leises Rauschen – für den Benchmark (Whisper-Encoder-Kosten hängen kaum vom Inhalt ab).</summary>
    public static void WriteNoise(string path, TimeSpan duration, int sampleRate = 16000, int seed = 42)
    {
        var samples = (int)(duration.TotalSeconds * sampleRate);
        var rnd = new Random(seed);
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        var dataBytes = samples * 2;
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);          // PCM
        w.Write((short)1);          // mono
        w.Write(sampleRate);
        w.Write(sampleRate * 2);    // byte rate
        w.Write((short)2);          // block align
        w.Write((short)16);         // bits
        w.Write("data"u8);
        w.Write(dataBytes);
        for (var i = 0; i < samples; i++) w.Write((short)rnd.Next(-200, 200));
    }
}
