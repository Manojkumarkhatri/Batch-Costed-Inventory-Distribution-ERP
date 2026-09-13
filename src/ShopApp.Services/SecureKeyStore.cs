using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace ShopApp.Services;

/// <summary>
/// Stores the derived backup key using Windows DPAPI, tied to the owner's
/// Windows account. He types the passphrase once; after that backups run
/// silently.
///
/// IMPORTANT: DPAPI is machine and account bound. On a NEW PC after a crash
/// it cannot help - he must type the passphrase to restore. That is why the
/// setup screen forces him to confirm he has written it down on paper.
/// Lose the passphrase and every backup is permanently unreadable.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SecureKeyStore
{
    private static readonly byte[] Entropy = "ShopApp.Backup.v1"u8.ToArray();

    public static byte[] Protect(byte[] key) =>
        ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser);

    public static byte[] Unprotect(byte[] protectedKey) =>
        ProtectedData.Unprotect(protectedKey, Entropy, DataProtectionScope.CurrentUser);

    public static bool TryUnprotect(byte[]? protectedKey, out byte[] key)
    {
        key = Array.Empty<byte>();
        if (protectedKey is null || protectedKey.Length == 0) return false;
        try { key = Unprotect(protectedKey); return true; }
        catch (CryptographicException) { return false; }   // different Windows account
    }
}
