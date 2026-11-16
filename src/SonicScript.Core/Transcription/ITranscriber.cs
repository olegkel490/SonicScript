namespace SonicScript.Core.Transcription;

public sealed record TranscriptionRequest(string AudioPath, string? Language, string? Prompt);

/// <summary>Language = erkannte Sprache (ISO, z. B. "de"), falls bekannt.</summary>
public sealed record TranscriptionResult(string Text, string? Language);

public interface ITranscriber
{
    Task<TranscriptionResult> TranscribeAsync(TranscriptionRequest request, CancellationToken ct);
}
