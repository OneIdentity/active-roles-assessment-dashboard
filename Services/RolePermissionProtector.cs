using Microsoft.AspNetCore.DataProtection;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Encrypts and decrypts the editable role/permission matrix using ASP.NET Core Data
/// Protection. The matrix is serialized to JSON and the resulting string is protected into a
/// single portable payload stored in appsettings under
/// <c>ActiveRoles:Roles:ProtectedMatrix</c>, so the assignments are never persisted in
/// plaintext. Mirrors <see cref="ServiceAccountSecretProtector"/> but uses its own dedicated,
/// stable purpose string so the two payloads are not interchangeable.
/// </summary>
public class RolePermissionProtector
{
    // Stable, application-specific purpose string. Changing this invalidates existing
    // protected values, so keep it constant across releases.
    internal const string Purpose = "ActiveRolesDashboard.Roles.PermissionMatrix.v1";

    private readonly IDataProtector _protector;

    public RolePermissionProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    /// <summary>Encrypts a plaintext (JSON) matrix into a portable, Base64URL-encoded protected payload.</summary>
    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            throw new ArgumentException("Matrix to protect must not be empty.", nameof(plaintext));

        return _protector.Protect(plaintext);
    }

    /// <summary>
    /// Decrypts a protected payload produced by <see cref="Protect"/>. Returns the plaintext
    /// (JSON) matrix, or throws if the payload is missing, tampered with, or was protected with a
    /// different key ring / purpose. Callers should fall back to the default matrix on failure.
    /// </summary>
    public string Unprotect(string protectedPayload)
    {
        if (string.IsNullOrEmpty(protectedPayload))
            throw new InvalidOperationException(
                "ActiveRoles:Roles:ProtectedMatrix is not configured.");

        return _protector.Unprotect(protectedPayload);
    }
}
