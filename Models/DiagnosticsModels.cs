using System.Text.Json.Serialization;

namespace ActiveRolesDashboard.Models;

/// <summary>
/// The dashboard scope a diagnostics run is associated with. Determines which server
/// types and probe targets are eligible.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DiagnosticsDashboard
{
    ActiveRoles,
    ActiveDirectory,
    EntraId,
    Exchange
}

/// <summary>
/// The category of infrastructure a probe target represents. Used to filter a run
/// "by server type".
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DiagnosticsServerType
{
    ActiveRolesServer,
    Rsts,
    WebInterface,
    DomainController,
    GlobalCatalog,
    EntraId,
    ExchangeServer,
    SqlServer
}

/// <summary>
/// The kind of probe to execute against a target. Used to filter a run "by test type".
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DiagnosticsTestType
{
    /// <summary>ICMP ping with a TCP-connect fallback on the target's primary port.</summary>
    Ping,
    /// <summary>LDAP bind (service account) to RootDSE.</summary>
    LdapBind,
    /// <summary>Active Roles REST GET of the target directory object.</summary>
    RestGet,
    /// <summary>RSTS OAuth2 token request (authentication) using the service account.</summary>
    RstsAuth,
    /// <summary>Plain HTTP GET of the Web Interface site.</summary>
    WebHttp,
    /// <summary>HTTP GET of the Entra OpenID Connect discovery (well-known) document.</summary>
    EntraOpenId,
    /// <summary>SQL connect + SELECT against the ARServices table of the Active Roles config database.</summary>
    SqlQuery
}

/// <summary>Outcome classification for a single probe result.</summary>
public enum DiagnosticsStatus
{
    Ok,
    Warn,
    Fail,
    Skipped
}

/// <summary>
/// A concrete probe target (a single server / endpoint) discovered from configuration
/// or collected dashboard data.
/// </summary>
public class DiagnosticsTarget
{
    public DiagnosticsServerType ServerType { get; set; }

    /// <summary>Human-friendly display name (server name, host, or endpoint label).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Host name or IP used by ping / TCP / LDAP probes. May be empty for pure-URL targets.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Full URL used by REST / HTTP / OpenID probes. May be empty for host-only targets.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Distinguished name / object identity used by the REST GET probe, when applicable.</summary>
    public string Dn { get; set; } = string.Empty;

    /// <summary>Primary TCP port used for the ping TCP fallback (e.g. 636/389/3268/443).</summary>
    public int PrimaryPort { get; set; }

    /// <summary>Test types applicable to this target.</summary>
    public List<DiagnosticsTestType> ApplicableTests { get; set; } = new();

    /// <summary>Stable identifier used by the UI to select individual targets for a run.</summary>
    public string Id { get; set; } = string.Empty;
}

/// <summary>A request to run diagnostics, optionally filtered by server type, test type, or explicit targets.</summary>
public class DiagnosticsRequest
{
    public DiagnosticsDashboard Dashboard { get; set; }

    /// <summary>When set, only targets of this server type are probed.</summary>
    public DiagnosticsServerType? ServerType { get; set; }

    /// <summary>When set, only probes of this test type are executed.</summary>
    public DiagnosticsTestType? TestType { get; set; }

    /// <summary>When non-empty, only targets whose <see cref="DiagnosticsTarget.Id"/> is listed are probed.</summary>
    public List<string> TargetIds { get; set; } = new();
}

/// <summary>The result of a single probe (one test against one target).</summary>
public class DiagnosticsProbeResult
{
    public string TargetId { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public DiagnosticsServerType ServerType { get; set; }
    public DiagnosticsTestType TestType { get; set; }
    public DiagnosticsStatus Status { get; set; }

    /// <summary>Round-trip latency in milliseconds, when measurable.</summary>
    public long? LatencyMs { get; set; }

    /// <summary>Short human-readable detail (e.g. "ICMP blocked, TCP 636 ok" or an error message).</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>The aggregate result of a diagnostics run.</summary>
public class DiagnosticsResult
{
    public DiagnosticsDashboard Dashboard { get; set; }
    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CompletedUtc { get; set; }
    public List<DiagnosticsProbeResult> Probes { get; set; } = new();

    [JsonIgnore] public int OkCount => Probes.Count(p => p.Status == DiagnosticsStatus.Ok);
    [JsonIgnore] public int WarnCount => Probes.Count(p => p.Status == DiagnosticsStatus.Warn);
    [JsonIgnore] public int FailCount => Probes.Count(p => p.Status == DiagnosticsStatus.Fail);
}
