using NAudio.Wave;

namespace SonicScript.Platform.Windows;

public enum Cue
{
    Start,
    Stop,
    Cancel,
    Error,
}

/// <summary>Kurze, leise Töne für Aufnahme-Start/-Ende. Werden einmal erzeugt (keine Sounddateien nötig).</summary>
public static class Sounds
{
    private const int Rate = 44100;
    private static readonly Dictionary<Cue, byte[]> Cache = new()
    {
        [Cue.Start] = Tones((660, 70), (990, 90)),
        [Cue.Stop] = Tones((990, 70), (660, 90)),
        [Cue.Cancel] = Tones((440, 110)),
        [Cue.Error] = Tones((330, 120), (0, 60), (330, 120)),
    };

    public static void Play(Cue cue)
    {
        try
        {
            var data = Cache[cue];
            var output = new WaveOutEvent();
            var stream = new RawSourceWaveStream(new MemoryStream(data), new WaveFormat(Rate, 16, 1));
            output.Init(stream);
            output.PlaybackStopped += (_, _) => { output.Dispose(); stream.Dispose(); };
            output.Play();
        }
        catch (Exception)
        {
            // Kein Ausgabegerät o. Ä. – Töne sind nur Komfort.
        }
    }

    /// <summary>Sinustöne mit weicher Hüllkurve (kein Knacken), Lautstärke ~20 %.</summary>
    private static byte[] Tones(params (double Hz, int Ms)[] parts)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        foreach (var (hz, len) in parts)
        {
            var n = Rate * len / 1000;
            var fade = Math.Min(n / 4, Rate * 8 / 1000);
            for (var i = 0; i < n; i++)
            {
                var env = Math.Min(1.0, Math.Min(i, n - 1 - i) / (double)Math.Max(1, fade));
                var v = hz <= 0 ? 0 : Math.Sin(2 * Math.PI * hz * i / Rate) * env * 0.2;
                w.Write((short)(v * short.MaxValue));
            }
        }
        return ms.ToArray();
    }
}
