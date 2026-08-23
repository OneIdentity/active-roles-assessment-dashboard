using Microsoft.AspNetCore.DataProtection;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Encrypts and decrypts the collection service-account password using ASP.NET Core
/// Data Protection. The encrypted value is stored in appsettings under
/// <c>ActiveRoles:ServiceAccount:ProtectedPassword</c> and decrypted at runtime, so the
/// plaintext password is never persisted to disk.
///
/// Use <see cref="Protect"/> from the one-time protect utility (see Program.cs
/// "--protect-secret" switch) to generate the value to paste into appsettings.
/// </summary>
public class ServiceAccountSecretProtector
{
    // Stable, application-specific purpose string. Changing this invalidates existing
    // protected values, so keep it constant across releases.
    internal const string Purpose = "ActiveRolesDashboard.ServiceAccount.Password.v1";

    private readonly IDataProtector _protector;

    public ServiceAccountSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    /// <summary>Encrypts a plaintext secret into a portable, Base64URL-encoded protected payload.</summary>
    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            throw new ArgumentException("Secret to protect must not be empty.", nameof(plaintext));

        return _protector.Protect(plaintext);
    }

    /// <summary>
    /// Decrypts a protected payload produced by <see cref="Protect"/>. Returns the plaintext
    /// secret, or throws if the payload is missing, tampered with, or was protected with a
    /// different key ring / purpose.
    /// </summary>
    public string Unprotect(string protectedPayload)
    {
        if (string.IsNullOrEmpty(protectedPayload))
            throw new InvalidOperationException(
                "ActiveRoles:ServiceAccount:ProtectedPassword is not configured. " +
                "Run the application with '--protect-secret' to generate it.");

        return _protector.Unprotect(protectedPayload);
    }
}
