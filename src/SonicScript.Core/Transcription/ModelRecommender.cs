namespace SonicScript.Core.Transcription;

public sealed record HardwareInfo(bool GpuAccelerated, int LogicalCores);

/// <summary>
/// Empfiehlt ein Whisper-Modell. Ohne Messung nach Hardware; mit Benchmark nach Echtzeitfaktor
/// (Verarbeitungszeit / Audiolänge). Ziel: 10 s Diktat in deutlich unter 3 s.
/// </summary>
public static class ModelRecommender
{
    public const double TargetRealtimeFactor = 0.3;

    public static string RecommendInitial(HardwareInfo hw) =>
        hw.GpuAccelerated || hw.LogicalCores >= 12 ? "large-v3-turbo-q5_0"
        : hw.LogicalCores >= 6 ? "small"
        : "small-q5_1";

    /// <summary>Nach Benchmark: Modell behalten, wenn schnell genug, sonst eine Stufe kleiner (bis small-q5_1).</summary>
    public static string RecommendAfterBenchmark(string measuredModelId, double realtimeFactor)
    {
        if (realtimeFactor <= TargetRealtimeFactor) return measuredModelId;
        var ladder = new[] { "large-v3-turbo", "large-v3-turbo-q5_0", "medium-q5_0", "small", "small-q5_1", "base" };
        var idx = Array.IndexOf(ladder, measuredModelId);
        if (idx < 0) return "small";
        // Bei sehr langsamen Rechnern gleich zwei Stufen runter.
        var step = realtimeFactor > TargetRealtimeFactor * 3 ? 2 : 1;
        return ladder[Math.Min(ladder.Length - 1, idx + step)];
    }
}
