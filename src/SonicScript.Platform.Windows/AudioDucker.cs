using NAudio.CoreAudioApi;

namespace SonicScript.Platform.Windows;

/// <summary>
/// Stellt andere Audioquellen (Musik, Videos) während der Aufnahme leiser und danach wieder her.
/// Betrifft nur Apps, die gerade Ton abspielen; die eigene App und Systemtöne bleiben unverändert.
/// </summary>
public sealed class AudioDucker
{
    private readonly object _gate = new();
    private readonly List<(AudioSessionControl Session, float Volume)> _ducked = [];

    public void Duck(int levelPercent)
    {
        Task.Run(() =>
        {
            lock (_gate)
            {
                if (_ducked.Count > 0) return;
                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    var sessions = device.AudioSessionManager.Sessions;
                    var own = (uint)Environment.ProcessId;
                    var factor = Math.Clamp(levelPercent, 0, 100) / 100f;
                    for (var i = 0; i < sessions.Count; i++)
                    {
                        var s = sessions[i];
                        if (s.IsSystemSoundsSession || s.GetProcessID == own || s.State != NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive)
                            continue;
                        var volume = s.SimpleAudioVolume.Volume;
                        s.SimpleAudioVolume.Volume = volume * factor;
                        _ducked.Add((s, volume));
                    }
                }
                catch (Exception)
                {
                    // Kein Audiogerät / Zugriff verweigert – dann eben ohne Ducking.
                }
            }
        });
    }

    public void Restore()
    {
        Task.Run(() =>
        {
            lock (_gate)
            {
                foreach (var (session, volume) in _ducked)
                {
                    try { session.SimpleAudioVolume.Volume = volume; }
                    catch (Exception) { /* Session inzwischen beendet */ }
                }
                _ducked.Clear();
            }
        });
    }
}
