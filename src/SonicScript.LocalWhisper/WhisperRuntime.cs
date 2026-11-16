using System.Reflection;
using Whisper.net.LibraryLoader;

namespace SonicScript.LocalWhisper;

/// <summary>
/// Entpackt die eingebetteten whisper.cpp-DLLs (CPU + Vulkan, win-x64) nach
/// <c>{baseDir}/whisper-runtime/{version}/runtimes/…</c> und zeigt Whisper.net per <see cref="RuntimeOptions.LibraryPath"/> dorthin.
/// <see cref="StartInBackground"/> beim Programmstart; <see cref="EnsureReady"/> vor dem ersten <c>WhisperFactory</c>.
/// </summary>
public static class WhisperRuntime
{
    private const string Prefix = "whisper-runtime/";
    private static readonly object Gate = new();
    private static string? _baseDirectory;
    private static bool _done;

    /// <summary>Entpacken im Hintergrund anstoßen (≈ 60 MB), damit der Start nicht blockiert.</summary>
    public static void StartInBackground(string baseDirectory)
    {
        _baseDirectory = baseDirectory;
        _ = Task.Run(EnsureReady);
    }

    /// <summary>Blockiert, bis die DLLs bereitliegen. Mehrfach aufrufbar.</summary>
    public static void EnsureReady()
    {
        lock (Gate)
        {
            if (_done) return;
            var baseDirectory = _baseDirectory ?? Path.Combine(Path.GetTempPath(), "SonicScript");
            var asm = typeof(WhisperRuntime).Assembly;
            var resources = asm.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).ToList();
            if (resources.Count > 0)
            {
                var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0";
                var plus = version.IndexOf('+');
                var root = Path.Combine(baseDirectory, "whisper-runtime", plus > 0 ? version[..plus] : version);
                foreach (var name in resources)
                {
                    var target = Path.Combine(root, "runtimes", name[Prefix.Length..].Replace('/', Path.DirectorySeparatorChar));
                    using var src = asm.GetManifestResourceStream(name)!;
                    if (File.Exists(target) && new FileInfo(target).Length == src.Length) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    var tmp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    using (var dst = File.Create(tmp)) src.CopyTo(dst);
                    File.Move(tmp, target, overwrite: true);
                }

                // Whisper.net nimmt das Verzeichnis dieses Pfads und sucht darunter runtimes/{vulkan/}win-x64/.
                RuntimeOptions.LibraryPath = Path.Combine(root, "whisper.dll");
            }
            _done = true;
        }
    }
}
