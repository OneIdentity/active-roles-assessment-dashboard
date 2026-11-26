namespace ActiveRolesDashboard.Services.PermissionTracker;

/// <summary>
/// Identifies trustees created by Exchange setup (/PrepareAD), whose ACEs are stamped across the
/// domain by Exchange and are therefore considered "Exchange default" permissions.
/// </summary>
public static class ExchangeDefaults
{
    private static readonly HashSet<string> Groups = new(StringComparer.OrdinalIgnoreCase)
    {
        "Exchange Trusted Subsystem",
        "Exchange Windows Permissions",
        "Exchange Servers",
        "Exchange Enterprise Servers",
        "Exchange Domain Servers",
        "Exchange Install Domain Servers",
        "Exchange Recipient Administrators",
        "Exchange Organization Administrators",
        "Exchange Public Folder Administrators",
        "Exchange View-Only Administrators",
        "Exchange Services",
        "Organization Management",
        "Recipient Management",
        "View-Only Organization Management",
        "Public Folder Management",
        "Delegated Setup",
        "Discovery Management",
        "Compliance Management",
        "Hygiene Management",
        "Records Management",
        "Server Management",
        "UM Management",
        "Managed Availability Servers",
        "ExchangeLegacyInterop",
    };

    /// <summary>True when <paramref name="trusteeName"/> (optionally DOMAIN\name or a DN) is an Exchange setup group.</summary>
    public static bool IsExchangeTrustee(string? trusteeName)
    {
        if (string.IsNullOrWhiteSpace(trusteeName)) return false;
        var name = trusteeName.Trim();
        if (name.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
        {
            var end = name.IndexOf(',');
            name = end > 3 ? name[3..end] : name[3..];
        }
        var slash = name.LastIndexOf('\\');
        if (slash >= 0) name = name[(slash + 1)..];
        var at = name.IndexOf('@');
        if (at > 0) name = name[..at];
        return Groups.Contains(name);
    }
}
