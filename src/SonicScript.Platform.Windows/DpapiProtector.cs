using System.Security.Cryptography;
using SonicScript.Core.Storage;

namespace SonicScript.Platform.Windows;

/// <summary>Windows DPAPI, an das Benutzerkonto gebunden.</summary>
public sealed class DpapiProtector : ISecretProtector
{
    private static readonly byte[] Entropy = "SonicScript.v1"u8.ToArray();

    public byte[] Protect(byte[] plain) => ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] cipher) => ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
}
