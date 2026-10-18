using System.Security.Cryptography;
using System.Text;

namespace Lancir.Core.Services;

/// <summary>
/// Protects small secrets (API keys) stored in settings.json with the Windows Data Protection API, so the file
/// alone is useless on another PC or user account. Output is base64 text, safe to keep in JSON.
/// </summary>
public static class SecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Lancir.Settings.v1");

    /// <summary>Encrypts <paramref name="secret"/> for the current Windows user; null for an empty secret.</summary>
    public static string? Protect(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || !OperatingSystem.IsWindows())
        {
            return null;
        }

        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret.Trim()), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>Decrypts a value from <see cref="Protect"/>; null when empty, corrupt or from another user.</summary>
    public static string? Unprotect(string? protectedText)
    {
        if (string.IsNullOrWhiteSpace(protectedText) || !OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedText), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
