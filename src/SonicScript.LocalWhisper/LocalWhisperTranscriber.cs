using System.Diagnostics;
using System.Text;
using SonicScript.Core.Audio;
using SonicScript.Core.Transcription;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace SonicScript.LocalWhisper;

/// <summary>
/// Lokale Transkription mit whisper.cpp über Whisper.net. Nutzt Vulkan (NVIDIA/AMD/Intel), wenn verfügbar,
/// sonst CPU. Das Modell wird einmal geladen und für alle Diktate wiederverwendet.
/// </summary>
public sealed class LocalWhisperTranscriber : ITranscriber, IDisposable
{
    private static readonly object RuntimeGate = new();
    private static bool _runtimeConfigured;

    private readonly string _modelPath;
    private readonly bool _useGpu;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WhisperFactory? _factory;

    public LocalWhisperTranscriber(string modelPath, bool useGpu)
    {
        _modelPath = modelPath;
        _useGpu = useGpu;
        ConfigureRuntime(useGpu);
    }

    /// <summary>Die Bibliotheksreihenfolge lässt sich nur vor dem ersten Laden setzen (pro Prozess).</summary>
    private static void ConfigureRuntime(bool useGpu)
    {
        lock (RuntimeGate)
        {
            if (_runtimeConfigured) return;
            RuntimeOptions.RuntimeLibraryOrder = useGpu
                ? [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx]
                : [RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx];
            _runtimeConfigured = true;
        }
    }

    /// <summary>Welche native Bibliothek tatsächlich geladen wurde (nach dem ersten Laden).</summary>
    public static string? LoadedRuntime => RuntimeOptions.LoadedLibrary?.ToString();

    public static bool IsGpuActive => RuntimeOptions.LoadedLibrary is RuntimeLibrary.Vulkan or RuntimeLibrary.Cuda or RuntimeLibrary.Cuda12;

    /// <summary>Modell vorab laden (z. B. beim Start), damit das erste Diktat nicht wartet.</summary>
    public void Preload()
    {
        if (_factory is not null) return;
        _gate.Wait();
        try { EnsureFactory(); }
        finally { _gate.Release(); }
    }

    private WhisperFactory EnsureFactory()
    {
        if (_factory is not null) return _factory;
        if (!File.Exists(_modelPath)) throw new FileNotFoundException("model_missing", _modelPath);
        _factory = WhisperFactory.FromPath(_modelPath, new WhisperFactoryOptions { UseGpu = _useGpu });
        return _factory;
    }

    public async Task<TranscriptionResult> TranscribeAsync(TranscriptionRequest request, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var factory = await Task.Run(EnsureFactory, ct).ConfigureAwait(false);
            var builder = factory.CreateBuilder()
                .WithLanguage(string.IsNullOrWhiteSpace(request.Language) ? "auto" : request.Language)
                .WithThreads(Math.Clamp(Environment.ProcessorCount - 1, 1, 8))
                .WithNoContext();
            if (!string.IsNullOrWhiteSpace(request.Prompt)) builder = builder.WithPrompt(request.Prompt);

            await using var processor = builder.Build();
            await using var audio = File.OpenRead(request.AudioPath);

            var sb = new StringBuilder();
            string? language = null;
            await foreach (var segment in processor.ProcessAsync(audio, ct).ConfigureAwait(false))
            {
                language ??= segment.Language;
                sb.Append(segment.Text);
            }
            return new TranscriptionResult(sb.ToString().Trim(), string.IsNullOrEmpty(language) || language == "auto" ? null : language);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Misst den Echtzeitfaktor (Rechenzeit / Audiolänge) mit 10 s Rauschen. Erster Lauf lädt das Modell (nicht mitgemessen).
    /// </summary>
    public async Task<double> BenchmarkAsync(string tempDirectory, CancellationToken ct)
    {
        Directory.CreateDirectory(tempDirectory);
        var wav = Path.Combine(tempDirectory, $"bench-{Guid.NewGuid():N}.wav");
        var duration = TimeSpan.FromSeconds(10);
        WavFile.WriteNoise(wav, duration);
        try
        {
            await Task.Run(Preload, ct).ConfigureAwait(false);
            var sw = Stopwatch.StartNew();
            await TranscribeAsync(new TranscriptionRequest(wav, "de", null), ct).ConfigureAwait(false);
            return sw.Elapsed.TotalSeconds / duration.TotalSeconds;
        }
        finally
        {
            File.Delete(wav);
        }
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _factory = null;
        _gate.Dispose();
    }
}
