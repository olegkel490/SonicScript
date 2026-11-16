using SonicScript.Core.Storage;

namespace SonicScript.Core.Tests;

internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sonicscript-tests-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}

/// <summary>XOR-"Verschlüsselung" nur für Tests – prüft, dass nichts im Klartext auf der Platte landet.</summary>
internal sealed class FakeProtector : ISecretProtector
{
    public byte[] Protect(byte[] plain) => plain.Select(b => (byte)(b ^ 0x5A)).Prepend((byte)0xEE).ToArray();
    public byte[] Unprotect(byte[] cipher) =>
        cipher.Length > 0 && cipher[0] == 0xEE ? cipher.Skip(1).Select(b => (byte)(b ^ 0x5A)).ToArray() : throw new InvalidDataException();
}

internal sealed class FakeTime(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    private long _ticks;
    public void Advance(TimeSpan by) { _now += by; _ticks += (long)(by.TotalSeconds * TimestampFrequency); }
    public override DateTimeOffset GetUtcNow() => _now;
    public override long GetTimestamp() => _ticks;
    public override long TimestampFrequency => 1_000_000;
}
