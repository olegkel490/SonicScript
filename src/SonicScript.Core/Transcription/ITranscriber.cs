namespace SonicScript.Core.Transcription;

public sealed record TranscriptionRequest(string AudioPath, string? Language, string? Prompt);

/// <summary>Language = erkannte Sprache (ISO, z. B. "de"), falls bekannt.</summary>
/// <param name="Fallback">Von der Ersatz-Erkennung (lokal) geliefert, weil die eigentliche ausfiel.</param>
public sealed record TranscriptionResult(string Text, string? Language, bool Fallback = false);

public interface ITranscriber
{
    Task<TranscriptionResult> TranscribeAsync(TranscriptionRequest request, CancellationToken ct);
}
