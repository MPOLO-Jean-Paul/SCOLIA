using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace JPServices.Owner.Desktop;

/// <summary>DPAPI : le fichier n'est lisible que par ce compte Windows, sur cet ordinateur.</summary>
internal sealed class WindowsProtector : ISecretProtector
{
    private static readonly byte[] Entropy = "SCOLIA-Console/acces"u8.ToArray();

    public bool Available => OperatingSystem.IsWindows();

    public byte[] Protect(byte[] clear) => OperatingSystem.IsWindows()
        ? Seal(clear) : throw new PlatformNotSupportedException();

    public byte[] Unprotect(byte[] sealedData) => OperatingSystem.IsWindows()
        ? Open(sealedData) : throw new PlatformNotSupportedException();

    [SupportedOSPlatform("windows")]
    private static byte[] Seal(byte[] clear) =>
        ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] Open(byte[] sealedData) =>
        ProtectedData.Unprotect(sealedData, Entropy, DataProtectionScope.CurrentUser);
}
