using System.Runtime.InteropServices;
using static SonicScript.Platform.Windows.NativeMethods;

namespace SonicScript.Platform.Windows;

/// <summary>
/// Win32-Zwischenablage ohne WinForms/WPF. Kann den kompletten Inhalt (alle HGLOBAL-Formate, z. B. Text, HTML,
/// Bilder als DIB, Dateilisten) sichern und wiederherstellen.
/// </summary>
public static class ClipboardService
{
    // Formate mit GDI-Handles statt HGLOBAL – nicht kopierbar; Windows synthetisiert sie meist aus CF_DIB/CF_ENHMETAFILE.
    private static readonly HashSet<uint> SkippedFormats = [2, 3, 9, 14, 0x80, 0x82, 0x83, 0x8E];
    private const long MaxSnapshotBytes = 64L * 1024 * 1024;

    public sealed class Snapshot
    {
        internal List<(uint Format, byte[] Data)> Items { get; } = [];
        public bool IsEmpty => Items.Count == 0;
    }

    public static uint SequenceNumber => GetClipboardSequenceNumber();

    public static async Task<bool> SetTextAsync(string text)
    {
        text = SonicScript.Core.Text.Newlines.ToWindows(text);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (TrySetText(text)) return true;
            await Task.Delay(30).ConfigureAwait(true); // evtl. kurz von anderem Prozess gesperrt
        }
        return false;
    }

    private static bool TrySetText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero)) return false;
        try
        {
            EmptyClipboard();
            var bytes = (text.Length + 1) * 2;
            var h = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
            if (h == IntPtr.Zero) return false;
            var p = GlobalLock(h);
            if (p == IntPtr.Zero) { GlobalFree(h); return false; }
            try { Marshal.Copy((text + '\0').ToCharArray(), 0, p, text.Length + 1); }
            finally { GlobalUnlock(h); }

            if (SetClipboardData(CF_UNICODETEXT, h) == IntPtr.Zero)
            {
                GlobalFree(h); // bei Misserfolg gehört der Speicher weiter uns
                return false;
            }

            // Nicht in Windows-Zwischenablageverlauf / Cloud-Sync übernehmen.
            SetExcludeFromHistory();
            return true;
        }
        finally { CloseClipboard(); }
    }

    private static void SetExcludeFromHistory()
    {
        foreach (var name in new[] { "ExcludeClipboardContentFromMonitorProcessing", "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard" })
        {
            var fmt = RegisterClipboardFormat(name);
            if (fmt == 0) continue;
            var h = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)4);
            if (h == IntPtr.Zero) continue;
            var p = GlobalLock(h);
            if (p != IntPtr.Zero) { Marshal.WriteInt32(p, 0); GlobalUnlock(h); }
            if (SetClipboardData(fmt, h) == IntPtr.Zero) GlobalFree(h);
        }
    }

    public static string GetText()
    {
        if (!IsClipboardFormatAvailable(CF_UNICODETEXT) || !OpenClipboard(IntPtr.Zero)) return "";
        try
        {
            var h = GetClipboardData(CF_UNICODETEXT);
            if (h == IntPtr.Zero) return "";
            var p = GlobalLock(h);
            if (p == IntPtr.Zero) return "";
            try { return Marshal.PtrToStringUni(p) ?? ""; }
            finally { GlobalUnlock(h); }
        }
        finally { CloseClipboard(); }
    }

    public static Snapshot Capture()
    {
        var snap = new Snapshot();
        if (!OpenClipboard(IntPtr.Zero)) return snap;
        try
        {
            long total = 0;
            uint fmt = 0;
            while ((fmt = EnumClipboardFormats(fmt)) != 0)
            {
                if (SkippedFormats.Contains(fmt)) continue;
                var h = GetClipboardData(fmt);
                if (h == IntPtr.Zero) continue;
                var size = (long)(ulong)GlobalSize(h);
                if (size <= 0 || total + size > MaxSnapshotBytes) continue;
                var p = GlobalLock(h);
                if (p == IntPtr.Zero) continue;
                try
                {
                    var data = new byte[size];
                    Marshal.Copy(p, data, 0, (int)size);
                    snap.Items.Add((fmt, data));
                    total += size;
                }
                finally { GlobalUnlock(h); }
            }
        }
        finally { CloseClipboard(); }
        return snap;
    }

    public static bool Restore(Snapshot snap)
    {
        if (!OpenClipboard(IntPtr.Zero)) return false;
        try
        {
            EmptyClipboard();
            foreach (var (fmt, data) in snap.Items)
            {
                var h = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)data.Length);
                if (h == IntPtr.Zero) continue;
                var p = GlobalLock(h);
                if (p == IntPtr.Zero) { GlobalFree(h); continue; }
                try { Marshal.Copy(data, 0, p, data.Length); }
                finally { GlobalUnlock(h); }
                if (SetClipboardData(fmt, h) == IntPtr.Zero) GlobalFree(h);
            }
            return true;
        }
        finally { CloseClipboard(); }
    }

    public static void Clear()
    {
        if (!OpenClipboard(IntPtr.Zero)) return;
        try { EmptyClipboard(); }
        finally { CloseClipboard(); }
    }
}
