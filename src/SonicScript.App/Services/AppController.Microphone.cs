using Avalonia.Controls;
using SonicScript.App.Views;
using SonicScript.Core.Localization;
using SonicScript.Platform.Windows;

namespace SonicScript.App.Services;

/// <summary>Mikrofon schnell wechseln: Tray-Untermenü (beim Öffnen aktualisiert) und optionale Taste.</summary>
public sealed partial class AppController
{
    private IReadOnlyList<MicrophoneInfo> SafeMicrophones()
    {
        try { return AudioRecorder.Microphones(); }
        catch (Exception ex) { Log.Error("Microphones", ex); return []; }
    }

    /// <summary>Aktuell gewähltes Gerät; "" = Windows-Standard.</summary>
    private string CurrentMicrophoneName()
    {
        var t = Settings.Transcription;
        if (!string.IsNullOrWhiteSpace(t.MicrophoneName)) return t.MicrophoneName;
        if (t.MicrophoneDevice < 0) return "";
        var mics = SafeMicrophones();
        return t.MicrophoneDevice < mics.Count ? mics[t.MicrophoneDevice].Name : "";
    }

    private NativeMenuItem BuildMicrophoneMenu()
    {
        var current = CurrentMicrophoneName();
        var root = new NativeMenuItem($"{Strings.Get("tray.microphone")}: {(current.Length == 0 ? Strings.Get("settings.microphone.default") : current)}")
        {
            Menu = new NativeMenu(),
        };
        // Geräte erst beim Öffnen einlesen – so erscheint ein gerade eingestecktes Headset sofort.
        root.Menu.Opening += (_, _) => FillMicrophoneMenu(root.Menu);
        FillMicrophoneMenu(root.Menu);
        return root;
    }

    private void FillMicrophoneMenu(NativeMenu menu)
    {
        menu.Items.Clear();
        var current = CurrentMicrophoneName();
        void Add(string label, string name, int index)
        {
            var item = new NativeMenuItem(label)
            {
                ToggleType = NativeMenuItemToggleType.Radio,
                IsChecked = string.Equals(name, current, StringComparison.OrdinalIgnoreCase),
            };
            item.Click += (_, _) => SelectMicrophone(name, index);
            menu.Add(item);
        }
        Add(Strings.Get("settings.microphone.default"), "", -1);
        var mics = SafeMicrophones();
        if (mics.Count > 0) menu.Add(new NativeMenuItemSeparator());
        foreach (var m in mics) Add(m.Name, m.Name, m.Index);
        if (!Settings.MicrophoneHotkey.IsEmpty)
        {
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(new NativeMenuItem($"{Strings.Get("tray.microphone_next")}  ({Controls.HotkeyBox.Display(Settings.MicrophoneHotkey)})") { IsEnabled = false });
        }
    }

    public void SelectMicrophone(string name, int index)
    {
        Settings.Transcription.MicrophoneName = name;
        Settings.Transcription.MicrophoneDevice = index;
        _micMissingShown = false;
        SaveQuietly();
        RebuildTrayMenu();
        var label = name.Length == 0 ? Strings.Get("settings.microphone.default") : name;
        Log.Info("Microphone selected: " + (name.Length == 0 ? "default" : name));
        Toast.Show(Strings.Format("toast.microphone", label));
    }

    private string? _micSignature;

    /// <summary>Gerät an-/abgesteckt → Tray-Menü neu aufbauen (falls das Menü beim Öffnen nicht aktualisiert wird).</summary>
    private void RefreshMicrophonesIfChanged()
    {
        var sig = string.Join("|", SafeMicrophones().Select(m => m.Name));
        if (_micSignature is not null && sig != _micSignature) RebuildTrayMenu();
        _micSignature = sig;
    }

    /// <summary>Reihum: Windows-Standard → Gerät 1 → Gerät 2 → … → Standard.</summary>
    private void NextMicrophone()
    {
        if (_recording is not null) return;
        var names = new List<(string Name, int Index)> { ("", -1) };
        names.AddRange(SafeMicrophones().Select(m => (m.Name, m.Index)));
        var current = CurrentMicrophoneName();
        var i = names.FindIndex(n => string.Equals(n.Name, current, StringComparison.OrdinalIgnoreCase));
        var next = names[(Math.Max(i, 0) + 1) % names.Count];
        SelectMicrophone(next.Name, next.Index);
    }
}
