namespace GraphPerfTest;

/// <summary>
/// Root configuration bound from appsettings.json. Holds one or more Entra tenants to
/// benchmark, plus global options that control which object types are retrieved.
/// </summary>
public sealed class AppSettings
{
    public List<TenantSettings> Tenants { get; set; } = new();

    /// <summary>
    /// Maximum number of objects to page through per object type. Use 0 (default) for no
    /// cap - i.e. retrieve every object. A small cap is handy for quick smoke tests.
    /// </summary>
    public int MaxObjectsPerType { get; set; }

    /// <summary>
    /// When true, also measures retrieving each group's members (the expensive operation
    /// that is slow via the Active Roles REST API). When false, only group objects and
    /// their owners are retrieved.
    /// </summary>
    public bool IncludeGroupMembers { get; set; } = true;

    /// <summary>Page size requested from Graph ($top). Graph caps most collections at 999.</summary>
    public int PageSize { get; set; } = 999;

    /// <summary>
    /// Number of group member-retrieval requests to run in parallel. 1 (default) preserves
    /// the original strictly-sequential behaviour; higher values fan out the per-group calls
    /// to reduce the impact of per-request latency. Values are clamped to at least 1.
    /// </summary>
    public int MemberFetchConcurrency { get; set; } = 1;
}

/// <summary>
/// Per-tenant Microsoft Entra app-registration credentials for the OAuth2 client
/// credentials (app-only) flow. The app registration needs application permissions
/// (e.g. User.Read.All, Group.Read.All, GroupMember.Read.All) with admin consent.
///
/// Authentication method is chosen per tenant:
///  - If <see cref="CertificateThumbprint"/> or <see cref="CertificatePath"/> is set, a
///    certificate credential is used (reusing the same certificate Active Roles uses).
///  - Otherwise <see cref="ClientSecret"/> is used.
/// </summary>
public sealed class TenantSettings
{
    /// <summary>Friendly name used only for console output.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Directory (tenant) ID - a GUID or the tenant's *.onmicrosoft.com domain.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Application (client) ID of the app registration.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Client secret value for the app registration (secret-based auth).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Thumbprint of a certificate installed in the Windows certificate store to use for
    /// certificate-based auth. Looked up in CurrentUser then LocalMachine (My store).
    /// Non-hex characters (spaces) are ignored. Takes precedence over <see cref="ClientSecret"/>.
    /// </summary>
    public string CertificateThumbprint { get; set; } = string.Empty;

    /// <summary>
    /// Path to a PFX/PEM certificate file for certificate-based auth. Takes precedence over
    /// <see cref="ClientSecret"/>. Use <see cref="CertificatePassword"/> for PFX protection.
    /// </summary>
    public string CertificatePath { get; set; } = string.Empty;

    /// <summary>Password for the PFX referenced by <see cref="CertificatePath"/> (optional).</summary>
    public string CertificatePassword { get; set; } = string.Empty;

    /// <summary>True when any certificate-based option is configured for this tenant.</summary>
    public bool UsesCertificate =>
        !string.IsNullOrWhiteSpace(CertificateThumbprint) || !string.IsNullOrWhiteSpace(CertificatePath);
}
