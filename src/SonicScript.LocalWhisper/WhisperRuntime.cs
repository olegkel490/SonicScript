using System.Reflection;
using Whisper.net.LibraryLoader;

namespace SonicScript.LocalWhisper;

/// <summary>
/// Entpackt die eingebetteten whisper.cpp-DLLs (CPU + Vulkan, win-x64) nach
/// <c>{baseDir}/{version}/runtimes/…</c> und zeigt Whisper.net per <see cref="RuntimeOptions.LibraryPath"/> dorthin.
/// Muss vor dem ersten <c>WhisperFactory</c> aufgerufen werden.
/// </summary>
public static class WhisperRuntime
{
    private const string Prefix = "whisper-runtime/";
    private static readonly object Gate = new();
    private static bool _done;

    public static void Prepare(string baseDirectory)
    {
        lock (Gate)
        {
            if (_done) return;
            var asm = typeof(WhisperRuntime).Assembly;
            var resources = asm.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).ToList();
            if (resources.Count == 0)
            {
                _done = true; // z. B. Linux-Entwicklung: Whisper.net sucht selbst
                return;
            }

            var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0";
            var plus = version.IndexOf('+');
            var root = Path.Combine(baseDirectory, "whisper-runtime", plus > 0 ? version[..plus] : version);
            foreach (var name in resources)
            {
                var target = Path.Combine(root, "runtimes", name[Prefix.Length..].Replace('/', Path.DirectorySeparatorChar));
                using var src = asm.GetManifestResourceStream(name)!;
                if (File.Exists(target) && new FileInfo(target).Length == src.Length) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var tmp = target + ".tmp";
                using (var dst = File.Create(tmp)) src.CopyTo(dst);
                File.Move(tmp, target, overwrite: true);
            }

            // Whisper.net nimmt das Verzeichnis dieses Pfads und sucht darunter runtimes/{vulkan/}win-x64/.
            RuntimeOptions.LibraryPath = Path.Combine(root, "whisper.dll");
            _done = true;
        }
    }
}
