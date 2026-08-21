namespace ActiveRolesDashboard.Models;

/// <summary>
/// A MITRE ATT&CK tactic (the adversary's goal), used to group techniques on the
/// exposure view. Ordering roughly follows the ATT&CK Enterprise kill-chain.
/// </summary>
public enum MitreTactic
{
    InitialAccess,
    Execution,
    Persistence,
    PrivilegeEscalation,
    DefenseEvasion,
    CredentialAccess,
    Discovery,
    LateralMovement,
    Impact
}

/// <summary>Display metadata for MITRE tactics.</summary>
public static class MitreTacticInfo
{
    public static IReadOnlyList<MitreTactic> Ordered { get; } = new[]
    {
        MitreTactic.InitialAccess,
        MitreTactic.Execution,
        MitreTactic.Persistence,
        MitreTactic.PrivilegeEscalation,
        MitreTactic.DefenseEvasion,
        MitreTactic.CredentialAccess,
        MitreTactic.Discovery,
        MitreTactic.LateralMovement,
        MitreTactic.Impact
    };

    public static string DisplayName(MitreTactic tactic) => tactic switch
    {
        MitreTactic.InitialAccess => "Initial Access",
        MitreTactic.Execution => "Execution",
        MitreTactic.Persistence => "Persistence",
        MitreTactic.PrivilegeEscalation => "Privilege Escalation",
        MitreTactic.DefenseEvasion => "Defense Evasion",
        MitreTactic.CredentialAccess => "Credential Access",
        MitreTactic.Discovery => "Discovery",
        MitreTactic.LateralMovement => "Lateral Movement",
        MitreTactic.Impact => "Impact",
        _ => tactic.ToString()
    };

    public static string Id(MitreTactic tactic) => tactic switch
    {
        MitreTactic.InitialAccess => "TA0001",
        MitreTactic.Execution => "TA0002",
        MitreTactic.Persistence => "TA0003",
        MitreTactic.PrivilegeEscalation => "TA0004",
        MitreTactic.DefenseEvasion => "TA0005",
        MitreTactic.CredentialAccess => "TA0006",
        MitreTactic.Discovery => "TA0007",
        MitreTactic.LateralMovement => "TA0008",
        MitreTactic.Impact => "TA0040",
        _ => string.Empty
    };
}

/// <summary>
/// The level of exposure a technique carries given the current environment. Derived
/// from the counts of the KPIs mapped to the technique; purely indicative (this is a
/// visibility view, not a compliance grade).
/// </summary>
public enum ExposureLevel
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

/// <summary>
/// Maps a single dashboard KPI to a technique. <see cref="Weight"/> reflects how
/// strongly the KPI indicates exposure to the technique (High weight = a small count
/// still elevates exposure, e.g. any computer with unconstrained delegation).
/// </summary>
public class TechniqueKpiMapping
{
    public string KpiKey { get; init; } = string.Empty;
    /// <summary>Short label describing why this KPI contributes to the technique.</summary>
    public string Rationale { get; init; } = string.Empty;
    /// <summary>Count at or above which this KPI pushes the technique to High exposure on its own.</summary>
    public int HighThreshold { get; init; } = 10;
    /// <summary>Count at or above which this KPI raises the technique to at least Medium exposure.</summary>
    public int MediumThreshold { get; init; } = 1;
}

/// <summary>
/// A curated MITRE ATT&CK technique mapped to one or more dashboard KPIs. The library
/// is static (like <see cref="AssessmentRuleLibrary"/>); exposure is computed at runtime
/// from the live <see cref="DashboardSummary"/>.
/// </summary>
public class MitreTechnique
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public MitreTactic Tactic { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Mitigation { get; init; } = string.Empty;
    public IReadOnlyList<TechniqueKpiMapping> Mappings { get; init; } = Array.Empty<TechniqueKpiMapping>();
}

/// <summary>Static catalogue of MITRE techniques mapped to Active Directory KPIs.</summary>
public static class MitreTechniqueLibrary
{
    public static readonly IReadOnlyList<MitreTechnique> All = new List<MitreTechnique>
    {
        // --- Initial Access ----------------------------------------------------
        new()
        {
            Id = "T1078.004",
            Name = "Valid Accounts: Cloud Accounts",
            Tactic = MitreTactic.InitialAccess,
            Description = "Guest (external) identities and the groups that contain them provide an attacker-controllable entry vector into the tenant; guests inheriting group-based access widen the reachable resources from outside the organisation.",
            Mitigation = "Govern guest lifecycle with access reviews and expiration; restrict guest permissions and remove guests from privileged or broadly-scoped groups; limit which groups may contain guests.",
            Mappings = new[]
            {
                new TechniqueKpiMapping { KpiKey = "EntraGuestUsers", Rationale = "Guest (external) accounts are an external foothold into the tenant.", MediumThreshold = 10, HighThreshold = 50 },
                new TechniqueKpiMapping { KpiKey = "EntraGuestContainingGroups", Rationale = "Groups containing guests extend group-based access to external identities.", MediumThreshold = 1, HighThreshold = 10 }
            }
        },

        // --- Credential Access -------------------------------------------------
        new()
        {
            Id = "T1558.003",
            Name = "Kerberoasting",
            Tactic = MitreTactic.CredentialAccess,
            Description = "Adversaries request service tickets for accounts that expose a Service Principal Name and crack them offline to recover the account password.",
            Mitigation = "Minimise user accounts carrying an SPN; use Group Managed Service Accounts (gMSA) and enforce long, complex passwords.",
            Mappings = new[]
            {
                new TechniqueKpiMapping { KpiKey = "SpnUserAccounts", Rationale = "User accounts with an SPN are directly Kerberoastable.", MediumThreshold = 1, HighThreshold = 10 }
            }
        },
        new()
        {
            Id = "T1558.004",
            Name = "AS-REP Roasting",
            Tactic = MitreTactic.CredentialAccess,
            Description = "Accounts that do not require Kerberos pre-authentication allow an attacker to request encrypted material and crack it offline.",
            Mitigation = "Require Kerberos pre-authentication on all accounts.",
            Mappings = new[]
            {
                new TechniqueKpiMapping { KpiKey = "NoKerberosPreauth", Rationale = "Accounts without pre-auth are AS-REP roastable.", MediumThreshold = 1, HighThreshold = 5 }
            }
        },
        new()
        {
            Id = "T1555",
            Name = "Credentials from Password Stores",
            Tactic = MitreTactic.CredentialAccess,
            Description = "Reversible encryption and DES encryption store or transmit passwords in a recoverable/weak form, easing credential recovery.",
            Mitigation = "Disable reversible encryption and DES; require AES encryption types.",
            Mappings = new[]
            {
                new TechniqueKpiMapping { KpiKey = "ReversibleEncryption", Rationale = "Reversible encryption stores recoverable passwords.", MediumThreshold = 1, HighThreshold = 1 },
                new TechniqueKpiMapping { KpiKey = "UseDesEncryption", Rationale = "DES is cryptographically weak.", MediumThreshold = 1, HighThreshold = 5 }
            }
        },
        new()
        {
            Id = "T1110",
            Name = "Brute Force",
            Tactic = MitreTactic.CredentialAccess,
            Description = "Accounts that do not require a password, or whose passwords never expire, weaken resistance to guessing and reuse attacks.",
            Mitigation = "Require passwords on all accounts and limit non-expiring passwords to hardened service accounts.",
            Mappings = new[]
            {
                new TechniqueKpiMapping { KpiKey = "PasswordNotRequired", Rationale = "Accounts with no password requirement.", MediumThreshold = 1, HighThreshold = 1 },
                new TechniqueKpiMapping { KpiKey = "PasswordNeverExpires", Rationale = "Stale, non-rotating credentials.", MediumThreshold = 10, HighThreshold = 50 }
            }
        },

        // --- Privilege Escalation ---------------------------------------------
        new()
        {
            Id = "T1078.002",
            Name = "Valid Accounts: Domain Accounts",
            Tactic = MitreTactic.PrivilegeEscalation,
            Description = "Highly privileged group membership expands the blast radius of any compromised credential.",
            Mitigation = "Minimise and regularly review membership of privileged groups; apply tiered administration.",
            Mappings = new[]
            {
                new TechniqueKpiMapping { KpiKey = "DomainAdmins", Rationale = "Domain Admins hold full domain control.", MediumThreshold = 5, HighThreshold = 15 },
                new TechniqueKpiMapping { KpiKey = "EnterpriseAdmins", Rationale = "Enterprise Admins hold forest-wide control.", MediumThreshold = 2, HighThreshold = 5 },
                new TechniqueKpiMapping { KpiKey = "AdminCount", Rationale = "Accounts flagged as protected/privileged.", MediumThreshold = 20, HighThreshold = 75 }
            }
        },

        // --- Lateral Movement --------------------------------------------------
        new()
        {
            Id = "T1550",
            Name = "Use Alternate Authentication Material (Delegation Abuse)",
            Tactic = MitreTactic.LateralMovement,
            Description = "Unconstrained delegation lets a compromised host impersonate any user that authenticates to it, enabling credential theft and lateral movement.",
            Mitigation = "Remove unconstrained delegation from computers and users (except domain controllers); use constrained or resource-based constrained delegation.",
            Mappings = new[]
            {
                new TechniqueKpiMapping { KpiKey = "UnconstrainedComputers", Rationale = "Computers trusted for unconstrained delegation are high-value pivot points.", MediumThreshold = 1, HighThreshold = 1 },
                new TechniqueKpiMapping { KpiKey = "TrustedForDelegation", Rationale = "User accounts trusted for unconstrained delegation.", MediumThreshold = 1, HighThreshold = 5 }
            }
        },

        // --- Persistence -------------------------------------------------------
        new()
        {
            Id = "T1098",
            Name = "Account Manipulation",
            Tactic = MitreTactic.Persistence,
            Description = "Stale, orphaned, or unmanaged accounts (no manager, never logged in, deprovisioned but present) provide durable footholds that evade routine review.",
            Mitigation = "Enforce ownership and lifecycle management; disable and remove dormant accounts promptly.",
            Mappings = new[]
            {
                new TechniqueKpiMapping { KpiKey = "NeverLoggedIn", Rationale = "Dormant accounts are prime persistence candidates.", MediumThreshold = 5, HighThreshold = 25 },
                new TechniqueKpiMapping { KpiKey = "StaleUsers", Rationale = "Enabled but inactive accounts are durable, low-visibility footholds.", MediumThreshold = 5, HighThreshold = 25 },
                new TechniqueKpiMapping { KpiKey = "NoManagerServiceAccount", Rationale = "Unowned service accounts evade review.", MediumThreshold = 1, HighThreshold = 10 },
                new TechniqueKpiMapping { KpiKey = "DeprovisionedUsers", Rationale = "Deprovisioned-but-present accounts.", MediumThreshold = 1, HighThreshold = 10 }
            }
        },

        // --- Persistence: account creation / reactivation ---------------------
        new()
        {
            Id = "T1136",
            Name = "Create Account",
            Tactic = MitreTactic.Persistence,
            Description = "Accounts left in a deprovisioned or long-inactive state can be silently reactivated (or their residue reused) to establish persistent access that bypasses normal joiner controls.",
            Mitigation = "Complete the deprovisioning lifecycle by disabling and removing dormant accounts; alert on reactivation of previously deprovisioned or stale accounts.",
            Mappings = new[]
            {
                new TechniqueKpiMapping { KpiKey = "DeprovisionedUsers", Rationale = "Deprovisioned accounts that persist can be reactivated.", MediumThreshold = 10, HighThreshold = 50 },
                new TechniqueKpiMapping { KpiKey = "StaleUsers", Rationale = "Inactive enabled accounts are candidates for takeover/reuse.", MediumThreshold = 5, HighThreshold = 25 }
            }
        },

        // --- Lateral Movement: pass-the-ticket --------------------------------
        new()
        {
            Id = "T1550.003",
            Name = "Use Alternate Authentication Material: Pass the Ticket",
            Tactic = MitreTactic.LateralMovement,
            Description = "Service accounts with SPNs and accounts trusted for delegation expand the opportunities to forge or reuse Kerberos tickets (including Silver Tickets) for lateral movement.",
            Mitigation = "Reduce SPN-bearing user accounts (prefer gMSA), remove unnecessary delegation, and rotate service-account keys regularly.",
            Mappings = new[]
            {
                new TechniqueKpiMapping { KpiKey = "SpnUserAccounts", Rationale = "SPN user accounts enable Silver Ticket / ticket reuse.", MediumThreshold = 1, HighThreshold = 10 },
                new TechniqueKpiMapping { KpiKey = "TrustedForDelegation", Rationale = "Delegation-trusted accounts broaden ticket reuse.", MediumThreshold = 1, HighThreshold = 5 }
            }
        },

        // --- Privilege Escalation: legacy operator groups ---------------------
        new()
        {
            Id = "T1484.001",
            Name = "Domain Policy Modification: Group Policy Modification",
            Tactic = MitreTactic.PrivilegeEscalation,
            Description = "Membership in powerful legacy operator groups (Account, Server, Backup Operators) grants directory- or DC-level capabilities that can be abused to modify policy, sign in to controllers, or restore/read protected data.",
            Mitigation = "Keep legacy operator groups empty and delegate specific, least-privilege permissions instead; monitor membership changes.",
            Mappings = new[]
            {
                new TechniqueKpiMapping { KpiKey = "AccountOperators", Rationale = "Account Operators can manage most users and groups.", MediumThreshold = 1, HighThreshold = 3 },
                new TechniqueKpiMapping { KpiKey = "ServerOperators", Rationale = "Server Operators can sign in to and control domain controllers.", MediumThreshold = 1, HighThreshold = 3 },
                new TechniqueKpiMapping { KpiKey = "BackupOperators", Rationale = "Backup Operators can read/restore any file (escalation path).", MediumThreshold = 2, HighThreshold = 5 }
            }
        },

        // --- Defense Evasion ---------------------------------------------------
        new()
        {
            Id = "T1078",
            Name = "Valid Accounts (Expired / Locked)",
            Tactic = MitreTactic.DefenseEvasion,
            Description = "Expired or locked accounts that remain enabled create inconsistent state that can mask malicious re-use.",
            Mitigation = "Reconcile expired and locked accounts; disable rather than leave in an ambiguous state.",
            Mappings = new[]
            {
                new TechniqueKpiMapping { KpiKey = "ExpiredUsers", Rationale = "Expired accounts still present.", MediumThreshold = 5, HighThreshold = 25 },
                new TechniqueKpiMapping { KpiKey = "UserAccountLockedOut", Rationale = "Locked accounts may indicate active attacks.", MediumThreshold = 3, HighThreshold = 15 }
            }
        },

        // --- Discovery ---------------------------------------------------------
        new()
        {
            Id = "T1069",
            Name = "Permission Groups Discovery",
            Tactic = MitreTactic.Discovery,
            Description = "Empty or unowned groups clutter the directory and complicate the review of effective access an attacker can enumerate.",
            Mitigation = "Assign owners to all groups and remove empty groups to keep group scope auditable.",
            Mappings = new[]
            {
                new TechniqueKpiMapping { KpiKey = "NoGroupOwner", Rationale = "Unowned groups lack review.", MediumThreshold = 5, HighThreshold = 25 },
                new TechniqueKpiMapping { KpiKey = "EntraNoGroupOwner", Rationale = "Unowned Entra groups add unreviewed, enumerable access scope.", MediumThreshold = 5, HighThreshold = 25 },
                new TechniqueKpiMapping { KpiKey = "EmptyGroups", Rationale = "Empty groups add enumeration noise.", MediumThreshold = 10, HighThreshold = 50 }
            }
        }
    };

    public static IEnumerable<MitreTechnique> ForTactic(MitreTactic tactic) => All.Where(t => t.Tactic == tactic);
}

// --- Computed exposure view models ----------------------------------------------

/// <summary>A KPI's contribution to a technique's exposure, resolved against live data.</summary>
public class TechniqueKpiContribution
{
    public string KpiKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;
    public int Count { get; set; }
    public bool HasError { get; set; }
    public ExposureLevel Level { get; set; }
}

/// <summary>A technique with its computed exposure and per-KPI contributions.</summary>
public class TechniqueExposure
{
    public MitreTechnique Technique { get; set; } = new();
    public ExposureLevel Level { get; set; }
    public List<TechniqueKpiContribution> Contributions { get; set; } = new();
    public bool HasData => Contributions.Any(c => !c.HasError);
}

/// <summary>A tactic column containing its techniques' exposures.</summary>
public class TacticExposure
{
    public MitreTactic Tactic { get; set; }
    public List<TechniqueExposure> Techniques { get; set; } = new();
    public ExposureLevel MaxLevel => Techniques.Count == 0 ? ExposureLevel.None : Techniques.Max(t => t.Level);
}

/// <summary>The complete ATT&CK exposure view computed from a dashboard summary.</summary>
public class AttackExposureView
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public List<TacticExposure> Tactics { get; set; } = new();

    public int HighCount => Tactics.SelectMany(t => t.Techniques).Count(t => t.Level == ExposureLevel.High);
    public int MediumCount => Tactics.SelectMany(t => t.Techniques).Count(t => t.Level == ExposureLevel.Medium);
    public int LowCount => Tactics.SelectMany(t => t.Techniques).Count(t => t.Level == ExposureLevel.Low);
    public int CoveredTechniques => Tactics.SelectMany(t => t.Techniques).Count(t => t.Level > ExposureLevel.None);
    public int TotalTechniques => Tactics.SelectMany(t => t.Techniques).Count();
}

// --- Comparison models -----------------------------------------------------
//
// Exposure is a live projection, so it is not persisted. Historical exposure is
// recomputed on demand from saved KPI snapshots, then compared/trended here. The
// baseline ("from") and comparison ("to") sides are both AttackExposureView values
// produced by MitreExposureService; comparison is by technique id.

/// <summary>How a technique's exposure changed between the baseline and comparison side.</summary>
public enum ExposureDeltaDirection
{
    Unchanged,
    Increased,      // exposure level rose (worse)
    Decreased,      // exposure level fell (better)
    Added,          // technique present only on the comparison side
    Removed         // technique present only on the baseline side
}

/// <summary>The full result of comparing a baseline exposure view against another (or the current live view).</summary>
public class ExposureComparison
{
    public DateTime FromGeneratedAt { get; set; }
    public DateTime ToGeneratedAt { get; set; }

    /// <summary>Label for the baseline side (e.g. a snapshot timestamp/label).</summary>
    public string FromLabel { get; set; } = string.Empty;
    /// <summary>Label for the comparison side.</summary>
    public string ToLabel { get; set; } = string.Empty;

    /// <summary>True when the "to" side represents live/current values rather than a saved snapshot.</summary>
    public bool ToIsCurrent { get; set; }

    public List<ExposureComparisonTactic> Tactics { get; set; } = new();

    public int IncreasedCount { get; set; }
    public int DecreasedCount { get; set; }
    public int UnchangedCount { get; set; }
    public int AddedCount { get; set; }
    public int RemovedCount { get; set; }
}

public class ExposureComparisonTactic
{
    public MitreTactic Tactic { get; set; }
    public List<ExposureComparisonRow> Rows { get; set; } = new();
}

public class ExposureComparisonRow
{
    public string TechniqueId { get; set; } = string.Empty;
    public string TechniqueName { get; set; } = string.Empty;
    public ExposureLevel? FromLevel { get; set; }
    public ExposureLevel? ToLevel { get; set; }
    public ExposureDeltaDirection Direction { get; set; }
    public DeltaSentiment Sentiment { get; set; }
}

// --- Trend models ----------------------------------------------------------

/// <summary>
/// A time-series of exposure levels across saved snapshots, recomputed per snapshot
/// timestamp. Each technique carries one level per timestamp (null where no data), and
/// aggregate High/Medium/Low counts are provided for a summary chart.
/// </summary>
public class ExposureTrend
{
    /// <summary>Snapshot timestamps (oldest first) shared by all series.</summary>
    public List<string> Labels { get; set; } = new();

    /// <summary>Per-technique level series (level as 0..3, null where absent).</summary>
    public List<ExposureTrendTechnique> Techniques { get; set; } = new();

    /// <summary>Aggregate number of High-exposure techniques per timestamp.</summary>
    public List<int> HighCounts { get; set; } = new();
    /// <summary>Aggregate number of Medium-exposure techniques per timestamp.</summary>
    public List<int> MediumCounts { get; set; } = new();
    /// <summary>Aggregate number of Low-exposure techniques per timestamp.</summary>
    public List<int> LowCounts { get; set; } = new();
}

public class ExposureTrendTechnique
{
    public string TechniqueId { get; set; } = string.Empty;
    public string TechniqueName { get; set; } = string.Empty;
    public MitreTactic Tactic { get; set; }

    /// <summary>One value per label; the numeric exposure level (0=None..3=High), or null where absent.</summary>
    public List<int?> Values { get; set; } = new();
}
