namespace SonicScript.Core.Transcription;

public sealed record WhisperModelInfo(string Id, string FileName, long ApproxBytes, string Description, int QualityRank)
{
    public string Url => "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/" + FileName;
    public double ApproxMegabytes => ApproxBytes / 1024d / 1024d;
}

/// <summary>GGML-Modelle für whisper.cpp / Whisper.net.</summary>
public static class WhisperModelCatalog
{
    public static readonly IReadOnlyList<WhisperModelInfo> All =
    [
        new("base", "ggml-base.bin", 147_951_465, "Base – sehr schnell, einfache Qualität", 1),
        new("small-q5_1", "ggml-small-q5_1.bin", 190_085_487, "Small (q5) – schnell, gut für schwache Rechner", 2),
        new("small", "ggml-small.bin", 487_601_967, "Small – schnell, solide", 3),
        new("medium-q5_0", "ggml-medium-q5_0.bin", 539_212_467, "Medium (q5) – gut, langsamer", 4),
        new("large-v3-turbo-q5_0", "ggml-large-v3-turbo-q5_0.bin", 574_041_195, "Large v3 Turbo (q5) – empfohlen", 5),
        new("large-v3-turbo", "ggml-large-v3-turbo.bin", 1_624_555_275, "Large v3 Turbo – beste Qualität, groß", 6),
    ];

    public static WhisperModelInfo? Find(string? id) => All.FirstOrDefault(m => m.Id == id);

    public static WhisperModelInfo Default => Find("large-v3-turbo-q5_0")!;

    /// <summary>GGML-Dateien beginnen mit der Magic-Zahl 0x67676d6c ("lmgg" little-endian).</summary>
    public static bool HasGgmlMagic(ReadOnlySpan<byte> head) =>
        head.Length >= 4 && BitConverter.ToUInt32(head[..4]) == 0x67676d6c;
}
