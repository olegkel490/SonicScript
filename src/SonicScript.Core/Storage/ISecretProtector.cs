namespace SonicScript.Core.Storage;

/// <summary>Verschlüsselung an den Benutzer gebunden (Windows: DPAPI).</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plain);
    byte[] Unprotect(byte[] cipher);
}
