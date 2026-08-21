namespace ActiveRolesDashboard.Models;

/// <summary>
/// The subject area an assessment targets. A rule may apply to more than one type
/// (e.g. an AD hygiene rule that also contributes to NIS2 posture).
/// </summary>
public enum AssessmentType
{
    ActiveDirectory,
    Entra,
    Nis2,
    Cis,
    Nist,
    Nist171,
    Nen7510,
    Iso27001,
    Gdpr,
    Dora,
    Hipaa,
    Sox,
    Tsa,
    Caf,
    CyberEssentials,
    Dspt,
    DfeCyber,
    PciDss,
    ActiveRoles
}

/// <summary>Display metadata for assessment types (labels shown in the UI and stored files).</summary>
public static class AssessmentTypeInfo
{
    public static IReadOnlyList<AssessmentType> All { get; } = new[]
    {
        AssessmentType.ActiveDirectory,
        AssessmentType.Entra,
        AssessmentType.Nis2,
        AssessmentType.Cis,
        AssessmentType.Nist,
        AssessmentType.Nist171,
        AssessmentType.Nen7510,
        AssessmentType.Iso27001,
        AssessmentType.Gdpr,
        AssessmentType.Dora,
        AssessmentType.Hipaa,
        AssessmentType.Sox,
        AssessmentType.Tsa,
        AssessmentType.Caf,
        AssessmentType.CyberEssentials,
        AssessmentType.Dspt,
        AssessmentType.DfeCyber,
        AssessmentType.PciDss,
        AssessmentType.ActiveRoles
    };

    public static string DisplayName(AssessmentType type) => type switch
    {
        AssessmentType.ActiveDirectory => "Active Directory",
        AssessmentType.Entra => "Entra ID",
        AssessmentType.Nis2 => "NIS2",
        AssessmentType.Cis => "CIS Benchmark",
        AssessmentType.Nist => "NIST CSF (Identity & Access)",
        AssessmentType.Nist171 => "NIST SP 800-171 (Access Control & Identification/Authentication - 3.1 & 3.5)",
        AssessmentType.Nen7510 => "NEN 7510 (Identity & Access)",
        AssessmentType.Iso27001 => "ISO/IEC 27001 (Access Control)",
        AssessmentType.Gdpr => "GDPR (Art. 32 Security of Processing)",
        AssessmentType.Dora => "DORA (ICT Access Controls & Resilience)",
        AssessmentType.Hipaa => "HIPAA (Security Rule Access Controls)",
        AssessmentType.Sox => "SOX (IT General Controls - Access & Change)",
        AssessmentType.Tsa => "TSA (Management-Plane Access & Privilege)",
        AssessmentType.Caf => "CAF (Identity & Access Control - Principle B2)",
        AssessmentType.CyberEssentials => "Cyber Essentials (User Access Control)",
        AssessmentType.Dspt => "DSPT (Managing Data Access - NDG Standard 4)",
        AssessmentType.DfeCyber => "DfE Cyber Standards (Account & Access Management)",
        AssessmentType.PciDss => "PCI DSS (Requirements 7 & 8 - Access Control)",
        AssessmentType.ActiveRoles => "Active Roles Configuration",
        _ => type.ToString()
    };

    /// <summary>
    /// Optional scope/disclaimer text for an assessment type. Returned as a non-empty string
    /// only for frameworks that need an explicit scope statement (currently GDPR, DORA, HIPAA, SOX, TSA, CAF, Cyber Essentials, DSPT and DfE Cyber Standards); empty otherwise.
    /// Shown as a banner in the Assessments UI and as a leading section in exported reports.
    /// </summary>
    public static string Description(AssessmentType type) => type switch
    {
        AssessmentType.Entra =>
            "This assessment evaluates Entra ID identity-governance indicators surfaced through Active Roles, " +
            "focusing on group ownership and review (unowned groups, single-owner groups) and external/guest " +
            "access (groups containing guests, guest accounts) plus manager accountability on user accounts. " +
            "The group-membership checks require Entra group membership to be fully loaded before they are " +
            "accurate. It does NOT assess Conditional Access, MFA/authentication methods, PIM/eligible role " +
            "assignments, sign-in risk, app registrations/consent, or licensing, and it is not a determination " +
            "of Entra ID security posture.",
        AssessmentType.Gdpr =>
            "This assessment evaluates identity and access-management controls in Active Directory and Active Roles " +
            "that contribute to GDPR Article 32 (security of processing) and accountability under Articles 5(2) and 24. " +
            "It does NOT assess lawful basis, consent, data-subject rights, DPIAs, records of processing, processor " +
            "agreements, breach-notification processes, or international data-transfer mechanisms, and it is not a " +
            "determination of GDPR compliance.",
        AssessmentType.Dora =>
            "This assessment evaluates identity/access-management controls and control-plane resilience in Active " +
            "Directory and Active Roles that support DORA (Regulation (EU) 2022/2554) ICT risk management, primarily " +
            "the protection and prevention controls of Article 9 and related resilience of the administration platform. " +
            "It does NOT assess ICT incident classification and reporting, digital operational resilience testing " +
            "(including threat-led penetration testing), ICT third-party risk management, business continuity, backups, " +
            "or information sharing, and it is not a determination of DORA compliance.",
        AssessmentType.Hipaa =>
            "This assessment evaluates identity and access-management controls in Active Directory and Active Roles " +
            "that support the HIPAA Security Rule technical and administrative safeguards for access control and " +
            "workforce access management (45 CFR 164.308(a)(3)-(4) and 164.312(a),(d)). It does NOT identify or scope " +
            "electronic protected health information (ePHI), and it does NOT assess audit controls, integrity or " +
            "encryption of ePHI, physical safeguards, risk analysis documentation, business associate agreements, " +
            "sanction policies, or breach notification. It is an identity/access hygiene indicator only and is not a " +
            "determination of HIPAA compliance.",
        AssessmentType.Sox =>
            "This assessment evaluates identity/access and change-management controls in Active Directory and Active " +
            "Roles that support Sarbanes-Oxley IT General Controls (ITGC), specifically 'access to programs and data' " +
            "and 'change management' as commonly framed under COBIT/COSO. It does NOT scope which accounts or systems " +
            "are relevant to financial reporting, and it does NOT assess access-review/attestation evidence, approval " +
            "or ticketing linkage, segregation-of-duties conflict matrices, audit logging, IT operations, backups, or " +
            "program development. It is an ITGC indicator only and is not a determination of SOX compliance or a " +
            "controls-effectiveness audit.",
        AssessmentType.Tsa =>
            "This assessment evaluates identity/access and privileged-access controls in Active Directory and Active " +
            "Roles that protect the identity and administration (management) plane, aligned to the privileged-access " +
            "and access-control themes of the UK Telecommunications Security Act 2021 and its Code of Practice. It " +
            "applies only to organisations in scope of the TSA (UK public telecoms providers) and only to the " +
            "management plane; it does NOT identify or scope 'security critical functions', network equipment, the " +
            "signalling plane, monitoring and analysis, supply-chain/vendor security, or patching of network " +
            "equipment, and it is not a determination of TSA compliance.",
        AssessmentType.Caf =>
            "This assessment provides supporting indicators for the NCSC Cyber Assessment Framework (CAF) Principle " +
            "B2 (Identity and Access Control), evaluating identity, privileged-access and access-management controls " +
            "in Active Directory and Active Roles. It does NOT produce CAF outcome ratings (Achieved / Partially " +
            "Achieved / Not Achieved), does NOT scope essential functions, and does NOT cover the other CAF " +
            "objectives and principles (governance and risk management, asset and supply-chain management, data " +
            "security, resilient networks, staff awareness, security monitoring, or incident response). It is an " +
            "identity and access-control indicator only and is not a determination of CAF compliance.",
        AssessmentType.CyberEssentials =>
            "This assessment provides supporting indicators for the UK Cyber Essentials scheme, primarily the 'User " +
            "Access Control' technical control (and, in part, 'Secure Configuration'), by evaluating identity, " +
            "privileged-access and account-hygiene controls in Active Directory and Active Roles. It does NOT address " +
            "the firewalls/boundary, malware protection, or security update management (patching) controls, does NOT " +
            "verify device-level configuration, and is NOT a Cyber Essentials or Cyber Essentials Plus certification.",
        AssessmentType.Dspt =>
            "This assessment provides supporting indicators for the UK Data Security and Protection Toolkit (DSPT), " +
            "primarily the National Data Guardian 'Managing Data Access' standard (Standard 4) and, in part, secure " +
            "configuration and personal-confidential-data access accountability, by evaluating identity, " +
            "privileged-access and account-hygiene controls in Active Directory and Active Roles. It does NOT address " +
            "staff training, policies, incident response, business continuity, supplier assurance, or physical " +
            "security, and it does NOT produce a DSPT status (Standards Met / Approaching Standards / Not Met). It is " +
            "an identity and access-control indicator only and is not a DSPT submission or determination.",
        AssessmentType.DfeCyber =>
            "This assessment provides supporting indicators for the UK Department for Education (DfE) Cyber Security " +
            "Standards for schools and colleges, primarily the account and access management standards (least " +
            "privilege, administrator account use, strong authentication, and account lifecycle) and, in part, " +
            "secure configuration, by evaluating identity, privileged-access and account-hygiene controls in Active " +
            "Directory and Active Roles. It does NOT address backups, boundary firewalls, anti-malware, security " +
            "update management (patching), user training, or incident response, and it does NOT produce a DfE " +
            "standards-met determination. It is an identity and access-control indicator only.",
        AssessmentType.PciDss =>
            "This assessment evaluates identity and access-management controls in Active Directory and Active Roles " +
            "that support PCI DSS Requirement 7 (restrict access to system components and cardholder data by " +
            "business need-to-know / least privilege) and Requirement 8 (identify users and authenticate access), " +
            "including privileged access, account hygiene and lifecycle (orphaned, stale, disabled and terminated " +
            "accounts), shared/generic and service accounts, and password/authentication configuration. It does NOT " +
            "identify or scope the cardholder data environment (CDE) or connected systems, and it does NOT assess " +
            "network segmentation, multi-factor authentication, logging and monitoring (Req 10), vulnerability and " +
            "patch management, encryption of account data, or physical security. It is an identity and access " +
            "hygiene indicator only and is not a determination of PCI DSS compliance, nor a QSA/ASV assessment.",
        AssessmentType.Nist171 =>
            "This assessment evaluates identity and access-management controls in Active Directory and Active Roles " +
            "that support NIST SP 800-171, specifically the Access Control (3.1) and Identification and " +
            "Authentication (3.5) requirement families - least privilege and need-to-know, privileged access, " +
            "account hygiene and lifecycle (orphaned, stale, disabled and terminated accounts), shared/generic and " +
            "service accounts, and password/authentication configuration. It does NOT identify or scope Controlled " +
            "Unclassified Information (CUI) or the systems that process it, and it does NOT assess the other " +
            "requirement families (including Audit and Accountability 3.3, Incident Response 3.6, Configuration " +
            "Management 3.4 beyond the reused secure-configuration indicators, Media Protection 3.8, Physical " +
            "Protection 3.10, or Awareness and Training 3.2). It is an identity and access hygiene indicator only " +
            "and is not a determination of NIST SP 800-171 compliance, a NIST SP 800-171A assessment, or a CMMC " +
            "assessment.",
        _ => string.Empty
    };
}

/// <summary>Severity of an assessment rule, driving scoring weight and ordering.</summary>
public enum AssessmentSeverity
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

/// <summary>Outcome of evaluating a single rule against the current data.</summary>
public enum AssessmentStatus
{
    Pass,
    Warning,
    Fail,
    NotApplicable // KPI errored or data unavailable
}

/// <summary>
/// Direction of the threshold comparison for a rule.
/// <see cref="AtLeast"/> (the default) treats a HIGHER count as worse: a count at or
/// above the fail/warn threshold fails/warns. <see cref="AtMost"/> inverts this and
/// treats a LOWER count as worse: a count at or below the fail/warn threshold
/// fails/warns. Use <see cref="AtMost"/> for adoption/coverage rules where more is
/// better (e.g. accounts enforcing strong authentication).
/// </summary>
public enum AssessmentComparison
{
    AtLeast,
    AtMost
}

/// <summary>
/// A single security/hygiene rule. It targets one KPI key and compares its count
/// against warn/fail thresholds. With the default <see cref="AssessmentComparison.AtLeast"/>
/// comparison, a count at or above <see cref="FailThreshold"/> fails and at or above
/// <see cref="WarnThreshold"/> warns. With <see cref="AssessmentComparison.AtMost"/>,
/// the comparison is inverted (a count at or below the threshold fails/warns).
/// </summary>
public class AssessmentRule
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string CategoryName { get; init; } = "General";
    public string KpiKey { get; init; } = string.Empty;
    public AssessmentSeverity Severity { get; init; } = AssessmentSeverity.Medium;

    /// <summary>Assessment types this rule applies to. Defaults to Active Directory.</summary>
    public IReadOnlyList<AssessmentType> Types { get; init; } = new[] { AssessmentType.ActiveDirectory };

    /// <summary>
    /// Direction of the threshold comparison. Defaults to <see cref="AssessmentComparison.AtLeast"/>
    /// (higher count is worse) to preserve existing rule behaviour.
    /// </summary>
    public AssessmentComparison Comparison { get; init; } = AssessmentComparison.AtLeast;

    /// <summary>Count at or above which the rule warns. Default 1 (any occurrence warns).</summary>
    public int WarnThreshold { get; init; } = 1;

    /// <summary>Count at or above which the rule fails. Default 1 (any occurrence fails).</summary>
    public int FailThreshold { get; init; } = 1;


    /// <summary>Human-readable guidance shown when the rule does not pass.</summary>
    public string Recommendation { get; init; } = string.Empty;
}

/// <summary>Built-in library of assessment rules grounded in the dashboard's risk KPIs.</summary>
public static class AssessmentRuleLibrary
{
    // Shared type groupings so a framework can be added in one place. "Compliance"
    // covers the security/least-privilege frameworks; AD-only rules stay operational.
    private static readonly AssessmentType[] Compliance =
    {
        AssessmentType.ActiveDirectory, AssessmentType.Nis2, AssessmentType.Cis,
        AssessmentType.Nist, AssessmentType.Nen7510, AssessmentType.Iso27001
    };

    private static readonly AssessmentType[] ComplianceNoCis =
    {
        AssessmentType.ActiveDirectory, AssessmentType.Nis2,
        AssessmentType.Nist, AssessmentType.Nen7510, AssessmentType.Iso27001
    };

    private static readonly AssessmentType[] AdOnly = { AssessmentType.ActiveDirectory };

    private static readonly AssessmentType[] ActiveRolesOnly = { AssessmentType.ActiveRoles };

    // Secure-configuration / "unsupported systems" control. Frameworks that explicitly
    // expect unsupported (end-of-life) software to be identified and remediated: the core
    // security baselines plus Cyber Essentials (Secure Configuration), DfE (secure config)
    // and DSPT (Standard 8 - unsupported systems). Includes AD for the operational view.
    private static readonly AssessmentType[] SecureConfigUnsupportedOs =
    {
        AssessmentType.ActiveDirectory, AssessmentType.Nis2, AssessmentType.Cis,
        AssessmentType.Nist, AssessmentType.Nen7510, AssessmentType.Iso27001,
        AssessmentType.CyberEssentials, AssessmentType.DfeCyber, AssessmentType.Dspt,
        AssessmentType.PciDss, AssessmentType.Nist171
    };

    // Core secure-configuration / authentication hardening controls (krbtgt hygiene and
    // domain password policy). Relevant to the security baselines and to the honestly
    // scoped frameworks that address secure configuration and authentication strength.
    private static readonly AssessmentType[] SecureConfigCore =
    {
        AssessmentType.ActiveDirectory, AssessmentType.Nis2, AssessmentType.Cis,
        AssessmentType.Nist, AssessmentType.Nen7510, AssessmentType.Iso27001,
        AssessmentType.CyberEssentials, AssessmentType.DfeCyber, AssessmentType.Dspt,
        AssessmentType.Dora, AssessmentType.Hipaa, AssessmentType.Sox, AssessmentType.PciDss, AssessmentType.Nist171
    };

    // GDPR (Art. 32 / accountability) reuses a curated subset of the security and
    // least-privilege rules that most directly support security of processing and
    // demonstrable access governance. These composite arrays add GDPR to the relevant
    // frameworks without pulling in every rule (see AssessmentTypeInfo.Description).
    private static readonly AssessmentType[] ComplianceGdpr =
    {
        AssessmentType.ActiveDirectory, AssessmentType.Nis2, AssessmentType.Cis,
        AssessmentType.Nist, AssessmentType.Nen7510, AssessmentType.Iso27001,
        AssessmentType.Gdpr, AssessmentType.Dora, AssessmentType.Hipaa, AssessmentType.Sox,
        AssessmentType.Tsa, AssessmentType.Caf, AssessmentType.CyberEssentials, AssessmentType.Dspt,
        AssessmentType.DfeCyber, AssessmentType.PciDss, AssessmentType.Nist171
    };

    private static readonly AssessmentType[] ComplianceNoCisGdpr =
    {
        AssessmentType.ActiveDirectory, AssessmentType.Nis2,
        AssessmentType.Nist, AssessmentType.Nen7510, AssessmentType.Iso27001,
        AssessmentType.Gdpr, AssessmentType.Dora, AssessmentType.Hipaa, AssessmentType.Sox,
        AssessmentType.Tsa, AssessmentType.Caf, AssessmentType.CyberEssentials, AssessmentType.Dspt,
        AssessmentType.DfeCyber, AssessmentType.PciDss, AssessmentType.Nist171
    };

    // Active Roles delegation / change-control rules that also evidence GDPR accountability,
    // DORA ICT protection (Art. 9) / change-control integrity, HIPAA workforce access
    // management (45 CFR 164.308(a)(3)-(4)), SOX ITGC access & change management, TSA
    // management-plane access control, CAF Principle B2 identity & access control,
    // Cyber Essentials user access control, DSPT NDG Standard 4 managing data access, and
    // DfE account & access management standards.
    private static readonly AssessmentType[] ActiveRolesGdpr =
    {
        AssessmentType.ActiveRoles, AssessmentType.Gdpr, AssessmentType.Dora,
        AssessmentType.Hipaa, AssessmentType.Sox, AssessmentType.Tsa, AssessmentType.Caf,
        AssessmentType.CyberEssentials, AssessmentType.Dspt, AssessmentType.DfeCyber,
        AssessmentType.PciDss, AssessmentType.Nist171
    };

    // Rules that are meaningful only under the GDPR lens (not part of another framework).
    private static readonly AssessmentType[] GdprOnly = { AssessmentType.Gdpr };

    // Active Roles resilience / single-point-of-failure rules that also evidence DORA
    // operational resilience of the administration control plane.
    private static readonly AssessmentType[] ActiveRolesDora =
    {
        AssessmentType.ActiveRoles, AssessmentType.Dora
    };

    // Rules that are meaningful only under the DORA lens (not part of another framework).
    private static readonly AssessmentType[] DoraOnly = { AssessmentType.Dora };

    // Rules that are meaningful only under the HIPAA lens (not part of another framework).
    private static readonly AssessmentType[] HipaaOnly = { AssessmentType.Hipaa };

    // Password/authentication lifecycle rules shared by HIPAA workforce access management,
    // PCI DSS Requirement 8 (identify users and authenticate access / credential lifecycle),
    // and NIST SP 800-171 Identification & Authentication (3.5).
    private static readonly AssessmentType[] CredentialLifecycleShared =
        { AssessmentType.Hipaa, AssessmentType.PciDss, AssessmentType.Nist171 };

    // Rules that are meaningful only under the SOX lens (not part of another framework).
    private static readonly AssessmentType[] SoxOnly = { AssessmentType.Sox };

    // Rules that are meaningful only under the TSA lens (not part of another framework).
    private static readonly AssessmentType[] TsaOnly = { AssessmentType.Tsa };

    // Rules that are meaningful only under the CAF lens (not part of another framework).
    private static readonly AssessmentType[] CafOnly = { AssessmentType.Caf };

    // Rules that are meaningful only under the Cyber Essentials lens (not part of another framework).
    private static readonly AssessmentType[] CyberEssentialsOnly = { AssessmentType.CyberEssentials };

    // Rules that are meaningful only under the DSPT lens (not part of another framework).
    private static readonly AssessmentType[] DsptOnly = { AssessmentType.Dspt };

    // Rules that are meaningful only under the DfE Cyber Standards lens (not part of another framework).
    private static readonly AssessmentType[] DfeCyberOnly = { AssessmentType.DfeCyber };

    // Entra ID identity-governance rules (group ownership/review and guest/external access).
    // These feed the dedicated Entra assessment plus every compliance framework that already
    // carries user/group governance rules (the same access-review and least-privilege lens as
    // the AD group/user-hygiene rules). Active Directory is intentionally excluded here because
    // it has its own AD-scoped group rules; the Entra type is always included so the Entra
    // assessment is populated. Any framework in this set that references a membership-dependent
    // Entra group KPI is automatically picked up by DependsOnEntraGroupMembership.
    private static readonly AssessmentType[] EntraGovernance =
    {
        AssessmentType.Entra, AssessmentType.Nis2, AssessmentType.Cis,
        AssessmentType.Nist, AssessmentType.Nen7510, AssessmentType.Iso27001,
        AssessmentType.Gdpr, AssessmentType.Dora, AssessmentType.Hipaa, AssessmentType.Sox,
        AssessmentType.Tsa, AssessmentType.Caf, AssessmentType.CyberEssentials,
        AssessmentType.Dspt, AssessmentType.DfeCyber, AssessmentType.PciDss, AssessmentType.Nist171
    };

    public static readonly IReadOnlyList<AssessmentRule> All = new List<AssessmentRule>
    {
        // --- Account security -------------------------------------------------
        new()
        {
            Id = "SEC-ReversibleEncryption",
            Title = "Users with reversible password encryption",
            CategoryName = "Account Security",
            KpiKey = "ReversibleEncryption",
            Severity = AssessmentSeverity.Critical,
            Types = Compliance,
            WarnThreshold = 1, FailThreshold = 1,
            Recommendation = "Disable reversible encryption on all user accounts; it stores passwords in a recoverable form."
        },
        new()
        {
            Id = "SEC-PasswordNotRequired",
            Title = "Users where a password is not required",
            CategoryName = "Account Security",
            KpiKey = "PasswordNotRequired",
            Severity = AssessmentSeverity.Critical,
            Types = Compliance,
            WarnThreshold = 1, FailThreshold = 1,
            Recommendation = "Clear the PASSWD_NOTREQD flag so all accounts require a password."
        },
        new()
        {
            Id = "SEC-NoKerberosPreauth",
            Title = "Accounts not requiring Kerberos pre-authentication",
            CategoryName = "Account Security",
            KpiKey = "NoKerberosPreauth",
            Severity = AssessmentSeverity.High,
            Types = Compliance,
            WarnThreshold = 1, FailThreshold = 1,
            Recommendation = "Require Kerberos pre-authentication to reduce exposure to AS-REP roasting."
        },
        new()
        {
            Id = "SEC-UseDesEncryption",
            Title = "Accounts using DES encryption",
            CategoryName = "Account Security",
            KpiKey = "UseDesEncryption",
            Severity = AssessmentSeverity.High,
            Types = Compliance,
            WarnThreshold = 1, FailThreshold = 1,
            Recommendation = "Disable DES; it is cryptographically weak. Use AES encryption types instead."
        },
        new()
        {
            Id = "SEC-TrustedForDelegation",
            Title = "Accounts trusted for unconstrained delegation",
            CategoryName = "Account Security",
            KpiKey = "TrustedForDelegation",
            Severity = AssessmentSeverity.High,
            Types = Compliance,
            WarnThreshold = 1, FailThreshold = 5,
            Recommendation = "Review unconstrained delegation; prefer constrained or resource-based delegation."
        },
        new()
        {
            Id = "SEC-SpnUserAccounts",
            Title = "User accounts with a Service Principal Name (Kerberoastable)",
            CategoryName = "Account Security",
            KpiKey = "SpnUserAccounts",
            Severity = AssessmentSeverity.High,
            Types = Compliance,
            WarnThreshold = 1, FailThreshold = 10,
            Recommendation = "Minimise user accounts carrying an SPN; enforce long, complex passwords or migrate to Group Managed Service Accounts (gMSA) to reduce Kerberoasting exposure."
        },
        new()
        {
            Id = "SEC-UnconstrainedComputers",
            Title = "Computers configured for unconstrained delegation",
            CategoryName = "Account Security",
            KpiKey = "UnconstrainedComputers",
            Severity = AssessmentSeverity.Critical,
            Types = ComplianceGdpr,
            WarnThreshold = 1, FailThreshold = 1,
            Recommendation = "Remove unconstrained delegation from computer accounts (excluding domain controllers); use constrained or resource-based constrained delegation to prevent credential theft and privilege escalation."
        },
        new()
        {
            Id = "SEC-PasswordNeverExpires",
            Title = "Users whose password never expires",
            CategoryName = "Account Security",
            KpiKey = "PasswordNeverExpires",
            Severity = AssessmentSeverity.Medium,
            Types = ComplianceGdpr,
            WarnThreshold = 5, FailThreshold = 25,
            Recommendation = "Limit non-expiring passwords to approved service accounts with strong secrets."
        },

        // --- Account hygiene --------------------------------------------------
        new()
        {
            Id = "HYG-ExpiredUsers",
            Title = "Expired user accounts still present",
            CategoryName = "Account Hygiene",
            KpiKey = "ExpiredUsers",
            Severity = AssessmentSeverity.Medium,
            Types = ComplianceNoCis,
            WarnThreshold = 1, FailThreshold = 10,
            Recommendation = "Disable or remove expired accounts to reduce the attack surface."
        },
        new()
        {
            Id = "HYG-NeverLoggedIn",
            Title = "Accounts that have never logged in",
            CategoryName = "Account Hygiene",
            KpiKey = "NeverLoggedIn",
            Severity = AssessmentSeverity.Low,
            Types = ComplianceNoCisGdpr,
            WarnThreshold = 5, FailThreshold = 25,
            Recommendation = "Review never-used accounts; remove stale provisioning that is no longer needed."
        },
        new()
        {
            Id = "HYG-UserAccountLockedOut",
            Title = "Currently locked-out user accounts",
            CategoryName = "Account Hygiene",
            KpiKey = "UserAccountLockedOut",
            Severity = AssessmentSeverity.Low,
            Types = AdOnly,
            WarnThreshold = 5, FailThreshold = 20,
            Recommendation = "Investigate repeated lockouts; they can indicate misconfiguration or brute-force activity."
        },
        new()
        {
            Id = "HYG-NoManagerUser",
            Title = "User accounts without a manager",
            CategoryName = "Account Hygiene",
            KpiKey = "NoManagerUser",
            Severity = AssessmentSeverity.Low,
            Types = AdOnly,
            WarnThreshold = 10, FailThreshold = 50,
            Recommendation = "Assign a manager to support access reviews and ownership accountability."
        },
        new()
        {
            Id = "HYG-NoManagerServiceAccount",
            Title = "Service accounts without a manager",
            CategoryName = "Account Hygiene",
            KpiKey = "NoManagerServiceAccount",
            Severity = AssessmentSeverity.Medium,
            Types = ComplianceNoCis,
            WarnThreshold = 1, FailThreshold = 10,
            Recommendation = "Assign an accountable owner to every service account."
        },

        // --- Group governance -------------------------------------------------
        new()
        {
            Id = "GRP-NoGroupOwner",
            Title = "Groups without an owner",
            CategoryName = "Group Governance",
            KpiKey = "NoGroupOwner",
            Severity = AssessmentSeverity.Medium,
            Types = AdOnly,
            WarnThreshold = 5, FailThreshold = 25,
            Recommendation = "Assign an owner (managedBy or secondary owner) to each group for access review."
        },
        new()
        {
            Id = "GRP-EmptyGroups",
            Title = "Empty groups",
            CategoryName = "Group Governance",
            KpiKey = "EmptyGroups",
            Severity = AssessmentSeverity.Low,
            Types = AdOnly,
            WarnThreshold = 10, FailThreshold = 50,
            Recommendation = "Remove or repurpose empty groups to reduce clutter and potential misuse."
        },

        // --- Entra ID identity governance -------------------------------------
        // Group ownership/review and guest/external access in Entra ID. These feed the
        // dedicated Entra assessment and the compliance frameworks that already carry
        // user/group governance rules (see the EntraGovernance type-set). The three group
        // rules depend on lazily loaded Entra group membership; the user/guest rules resolve
        // from eager Entra user data.
        new()
        {
            Id = "ENT-NoGroupOwner",
            Title = "Entra groups without an owner",
            CategoryName = "Entra Identity Governance",
            KpiKey = "EntraNoGroupOwner",
            Severity = AssessmentSeverity.Medium,
            Types = EntraGovernance,
            WarnThreshold = 5, FailThreshold = 25,
            Recommendation = "Assign an owner to every Entra group so membership can be periodically reviewed and access remains accountable. Unowned groups have no one responsible for attesting to who should have access."
        },
        new()
        {
            Id = "ENT-GuestContainingGroups",
            Title = "Entra groups containing guest (external) members",
            CategoryName = "Entra Identity Governance",
            KpiKey = "EntraGuestContainingGroups",
            Severity = AssessmentSeverity.High,
            Types = EntraGovernance,
            WarnThreshold = 5, FailThreshold = 25,
            Recommendation = "Review groups that include guest/external members and confirm the external access is still required and appropriately scoped. Guests in groups can inherit access to resources; remove stale guests and prefer entitlement-managed, time-bound access for external collaboration."
        },
        new()
        {
            Id = "ENT-SingleOwnerGroups",
            Title = "Entra groups with only a single owner",
            CategoryName = "Entra Identity Governance",
            KpiKey = "EntraSingleOwnerGroups",
            Severity = AssessmentSeverity.Low,
            Types = EntraGovernance,
            WarnThreshold = 10, FailThreshold = 50,
            Recommendation = "Assign at least two owners to each Entra group to avoid a single point of failure for access reviews and membership changes when the sole owner is unavailable or leaves the organisation."
        },
        new()
        {
            Id = "ENT-GuestUsers",
            Title = "Entra guest (external) user accounts",
            CategoryName = "Entra Identity Governance",
            KpiKey = "EntraGuestUsers",
            Severity = AssessmentSeverity.Medium,
            Types = EntraGovernance,
            WarnThreshold = 25, FailThreshold = 100,
            Recommendation = "Periodically review guest/external accounts and remove those that are no longer needed. Apply guest access reviews, expiration, and least-privilege scoping so external identities do not accumulate standing access to internal resources."
        },
        new()
        {
            Id = "ENT-NoManagerUser",
            Title = "Entra user accounts without a manager",
            CategoryName = "Entra Identity Governance",
            KpiKey = "EntraNoManagerUser",
            Severity = AssessmentSeverity.Low,
            Types = EntraGovernance,
            WarnThreshold = 10, FailThreshold = 50,
            Recommendation = "Populate the manager attribute on Entra user accounts to support access reviews, joiner/mover/leaver processes, and ownership accountability for delegated approvals."
        },

        // --- Secure configuration: unsupported / end-of-life operating systems -
        // These rules flag devices running operating systems that are past (or, for
        // Windows 10 22H2, reaching) end of support and no longer receive security
        // updates. Identifying and remediating unsupported software is an explicit
        // control in the security baselines and in Cyber Essentials, DfE and DSPT.
        new()
        {
            Id = "CFG-UnsupportedServerOs",
            Title = "Servers running unsupported operating systems",
            CategoryName = "Secure Configuration",
            KpiKey = "UnsupportedServerOs",
            Severity = AssessmentSeverity.High,
            Types = SecureConfigUnsupportedOs,
            WarnThreshold = 1, FailThreshold = 5,
            Recommendation = "Windows Server 2008 R2 and 2012 R2 are out of support and no longer receive security updates. Upgrade or decommission these servers, or isolate and place them under an extended-support agreement, to reduce exposure to unpatched vulnerabilities."
        },
        new()
        {
            Id = "CFG-UnsupportedClientOs",
            Title = "Clients running unsupported operating systems",
            CategoryName = "Secure Configuration",
            KpiKey = "UnsupportedClientOs",
            Severity = AssessmentSeverity.High,
            Types = SecureConfigUnsupportedOs,
            WarnThreshold = 1, FailThreshold = 10,
            Recommendation = "Windows 7, 8.1 and Windows 10 22H2 are out of (or reaching) end of support. Upgrade affected clients to a supported Windows release so they continue to receive security updates, or remove them from the environment."
        },

        // --- Secure configuration: stale computer accounts -------------------
        new()
        {
            Id = "CFG-StaleComputers",
            Title = "Stale (inactive) computer accounts",
            CategoryName = "Secure Configuration",
            KpiKey = "StaleComputers",
            Severity = AssessmentSeverity.Medium,
            Types = SecureConfigUnsupportedOs,
            WarnThreshold = 5, FailThreshold = 25,
            Recommendation = "Computer accounts that have not authenticated for an extended period are likely decommissioned or orphaned. Disable and remove stale computer accounts to reduce the attack surface and keep the directory accurate."
        },

        // --- Secure configuration: Kerberos (krbtgt) hygiene -----------------
        new()
        {
            Id = "CFG-KrbtgtPasswordAge",
            Title = "krbtgt password age (days)",
            CategoryName = "Secure Configuration",
            KpiKey = "KrbtgtPasswordAgeDays",
            Severity = AssessmentSeverity.High,
            Types = SecureConfigCore,
            WarnThreshold = 180, FailThreshold = 365,
            Recommendation = "A rarely rotated krbtgt account password increases exposure to Kerberos Golden Ticket attacks. Rotate the krbtgt password on a regular schedule (twice, with a delay between rotations) so forged tickets cannot remain valid indefinitely."
        },

        // --- Secure configuration: domain password policy --------------------
        // These policy checks are encoded as 0/1 weakness indicators (1 = weak) except
        // the max-age check, which is measured in days.
        new()
        {
            Id = "CFG-WeakPasswordLength",
            Title = "Minimum password length below 12 characters",
            CategoryName = "Secure Configuration",
            KpiKey = "WeakPasswordLength",
            Severity = AssessmentSeverity.High,
            Types = SecureConfigCore,
            WarnThreshold = 1, FailThreshold = 1,
            Recommendation = "The default domain minimum password length is below 12 characters. Increase the minimum length (12+ recommended) to strengthen resistance to password-guessing and offline cracking."
        },
        new()
        {
            Id = "CFG-PasswordComplexityDisabled",
            Title = "Password complexity disabled",
            CategoryName = "Secure Configuration",
            KpiKey = "PasswordComplexityDisabled",
            Severity = AssessmentSeverity.High,
            Types = SecureConfigCore,
            WarnThreshold = 1, FailThreshold = 1,
            Recommendation = "Password complexity is disabled in the default domain policy. Enable complexity so passwords must include a mix of character types, reducing the risk of weak, easily guessed passwords."
        },
        new()
        {
            Id = "CFG-NoAccountLockout",
            Title = "No account lockout threshold configured",
            CategoryName = "Secure Configuration",
            KpiKey = "NoAccountLockout",
            Severity = AssessmentSeverity.Medium,
            Types = SecureConfigCore,
            WarnThreshold = 1, FailThreshold = 1,
            Recommendation = "The account lockout threshold is zero, so accounts are never locked after repeated failed logons. Configure a lockout threshold to slow online password-guessing and brute-force attacks."
        },
        new()
        {
            Id = "CFG-PasswordMaxAge",
            Title = "Maximum password age (days)",
            CategoryName = "Secure Configuration",
            KpiKey = "PasswordMaxAgeDays",
            Severity = AssessmentSeverity.Low,
            Types = SecureConfigCore,
            WarnThreshold = 366, FailThreshold = 731,
            Recommendation = "The maximum password age is very long (or passwords never expire). Align the maximum password age with your policy; where long-lived passwords are used, ensure they are long, unique and backed by phishing-resistant MFA."
        },

        // --- Privileged access: AdminSDHolder / adminCount indicator ---------
        new()
        {
            Id = "PRIV-AdminCountAccounts",
            Title = "Accounts flagged with adminCount=1",
            CategoryName = "Privileged Access",
            KpiKey = "AdminCount",
            Severity = AssessmentSeverity.Medium,
            Types = ComplianceGdpr,
            WarnThreshold = 15, FailThreshold = 40,
            Recommendation = "Accounts with adminCount=1 are (or were) members of protected privileged groups and are governed by AdminSDHolder. A high or growing count can indicate privilege creep or orphaned protection. Review these accounts, remove unnecessary privileged group membership, and reset stale adminCount/protection where appropriate."
        },

        // --- Privileged access (least privilege) ------------------------------
        // These rules evaluate the size of highly privileged groups. The scoring
        // engine treats a higher count as worse, which aligns with least-privilege
        // intent: the fewer members in these groups, the smaller the blast radius.
        new()
        {
            Id = "PRIV-DomainAdmins",
            Title = "Domain Admins membership size",
            CategoryName = "Privileged Access",
            KpiKey = "DomainAdmins",
            Severity = AssessmentSeverity.High,
            Types = ComplianceGdpr,
            WarnThreshold = 5, FailThreshold = 10,
            Recommendation = "Keep Domain Admins to a small, named set of accounts. Remove standing membership, use just-in-time elevation, and review regularly."
        },
        new()
        {
            Id = "PRIV-EnterpriseAdmins",
            Title = "Enterprise Admins membership size",
            CategoryName = "Privileged Access",
            KpiKey = "EnterpriseAdmins",
            Severity = AssessmentSeverity.Critical,
            Types = ComplianceGdpr,
            WarnThreshold = 2, FailThreshold = 5,
            Recommendation = "Enterprise Admins should normally be empty except during forest-wide changes. Remove permanent members and elevate just-in-time."
        },
        new()
        {
            Id = "PRIV-SchemaAdmins",
            Title = "Schema Admins membership size",
            CategoryName = "Privileged Access",
            KpiKey = "SchemaAdmins",
            Severity = AssessmentSeverity.High,
            Types = Compliance,
            WarnThreshold = 1, FailThreshold = 3,
            Recommendation = "Schema Admins should be empty outside of schema-change windows. Remove standing membership and add temporarily only when required."
        },
        new()
        {
            Id = "PRIV-Administrators",
            Title = "Built-in Administrators membership size",
            CategoryName = "Privileged Access",
            KpiKey = "Administrators",
            Severity = AssessmentSeverity.High,
            Types = ComplianceGdpr,
            WarnThreshold = 8, FailThreshold = 15,
            Recommendation = "Constrain the built-in Administrators group to essential accounts. Prefer role-based delegation over broad administrative membership."
        },

        // --- Strong authentication (adoption / inverted) ----------------------
        // Inverted rule: a LOWER count is worse. This flags environments where few or
        // no accounts enforce smart-card (interactive MFA) logon. Note: SmartCardRequired
        // is a domain-wide count, so this is an adoption indicator rather than a
        // per-privileged-account check.
        new()
        {
            Id = "AUTH-SmartCardAdoption",
            Title = "Accounts enforcing smart-card (strong) authentication",
            CategoryName = "Strong Authentication",
            KpiKey = "SmartCardRequired",
            Severity = AssessmentSeverity.Medium,
            Types = new[] { AssessmentType.Nis2, AssessmentType.Nist, AssessmentType.Nen7510, AssessmentType.Iso27001 },
            Comparison = AssessmentComparison.AtMost,
            WarnThreshold = 2, FailThreshold = 0,
            Recommendation = "Enforce smart-card / phishing-resistant MFA for interactive logon, prioritising administrative and high-privilege accounts. A count of zero indicates strong authentication is not enforced anywhere."
        },

        // --- Account hygiene: stale / inactive accounts ----------------------
        new()
        {
            Id = "HYG-StaleAccounts",
            Title = "Stale (inactive) enabled accounts",
            CategoryName = "Account Hygiene",
            KpiKey = "StaleUsers",
            Severity = AssessmentSeverity.High,
            Types = ComplianceGdpr,
            WarnThreshold = 5, FailThreshold = 25,
            Recommendation = "Review enabled accounts with no interactive logon in the last {StaleThresholdDays} days. Disable or deprovision unused accounts to reduce the attack surface and align with least-privilege / lifecycle requirements."
        },

        // --- Additional account security -------------------------------------
        new()
        {
            Id = "SEC-CannotChangePassword",
            Title = "Users who cannot change their own password",
            CategoryName = "Account Security",
            KpiKey = "CannotChangePassword",
            Severity = AssessmentSeverity.Low,
            Types = ComplianceNoCis,
            WarnThreshold = 10, FailThreshold = 50,
            Recommendation = "Limit the 'user cannot change password' flag to approved service accounts; it prevents rotation of compromised credentials."
        },
        new()
        {
            Id = "SEC-MustChangePassword",
            Title = "Users flagged to change password at next logon",
            CategoryName = "Account Security",
            KpiKey = "MustChangePassword",
            Severity = AssessmentSeverity.Info,
            Types = AdOnly,
            WarnThreshold = 25, FailThreshold = 100,
            Recommendation = "A large backlog of 'must change password at next logon' accounts can indicate stalled onboarding or dormant provisioning; review and follow up."
        },

        // --- Delegation protection (inverted / adoption) ---------------------
        // SensitiveCannotDelegate is a protective control ("account is sensitive and
        // cannot be delegated"). Fewer protected accounts is worse, so this is inverted.
        new()
        {
            Id = "SEC-SensitiveCannotDelegateAdoption",
            Title = "Privileged accounts protected from delegation",
            CategoryName = "Delegation Protection",
            KpiKey = "SensitiveCannotDelegate",
            Severity = AssessmentSeverity.Medium,
            Types = new[] { AssessmentType.Nis2, AssessmentType.Nist, AssessmentType.Nen7510, AssessmentType.Iso27001 },
            Comparison = AssessmentComparison.AtMost,
            WarnThreshold = 2, FailThreshold = 0,
            Recommendation = "Mark administrative and high-value accounts as 'sensitive and cannot be delegated' (or add them to the Protected Users group) so their credentials cannot be forwarded via delegation. A count of zero indicates this protection is not applied anywhere."
        },

        // --- Account lifecycle: deprovisioned residue ------------------------
        new()
        {
            Id = "HYG-DeprovisionedUsers",
            Title = "Deprovisioned accounts still present",
            CategoryName = "Account Hygiene",
            KpiKey = "DeprovisionedUsers",
            Severity = AssessmentSeverity.Medium,
            Types = ComplianceNoCisGdpr,
            WarnThreshold = 10, FailThreshold = 50,
            Recommendation = "Complete the deprovisioning lifecycle: remove or archive accounts left in a deprovisioned state so they cannot be reactivated or misused."
        },

        // --- Privileged access: legacy operator groups -----------------------
        new()
        {
            Id = "PRIV-AccountOperators",
            Title = "Account Operators membership size",
            CategoryName = "Privileged Access",
            KpiKey = "AccountOperators",
            Severity = AssessmentSeverity.High,
            Types = Compliance,
            WarnThreshold = 1, FailThreshold = 3,
            Recommendation = "Account Operators is a powerful legacy group that can manage most user and group objects. Keep it empty and delegate specific permissions instead."
        },
        new()
        {
            Id = "PRIV-ServerOperators",
            Title = "Server Operators membership size",
            CategoryName = "Privileged Access",
            KpiKey = "ServerOperators",
            Severity = AssessmentSeverity.High,
            Types = Compliance,
            WarnThreshold = 1, FailThreshold = 3,
            Recommendation = "Server Operators can sign in to and control domain controllers. Keep this legacy group empty and use targeted delegation instead."
        },
        new()
        {
            Id = "PRIV-BackupOperators",
            Title = "Backup Operators membership size",
            CategoryName = "Privileged Access",
            KpiKey = "BackupOperators",
            Severity = AssessmentSeverity.Medium,
            Types = Compliance,
            WarnThreshold = 2, FailThreshold = 5,
            Recommendation = "Backup Operators can read and restore any file (a known privilege-escalation path). Restrict membership to dedicated backup service accounts."
        },

        // --- Active Roles configuration hygiene -------------------------------
        new()
        {
            Id = "AR-DisabledWorkflows",
            Title = "Disabled Active Roles workflows",
            CategoryName = "Active Roles Configuration",
            KpiKey = "DisabledWorkflows",
            Severity = AssessmentSeverity.Medium,
            Types = ActiveRolesGdpr,
            WarnThreshold = 1, FailThreshold = 3,
            Recommendation = "Disabled workflows no longer enforce their approval, provisioning or notification logic. Review disabled workflows and either re-enable or remove them so change controls remain in effect."
        },
        new()
        {
            Id = "AR-BroadDelegationLinks",
            Title = "Access Template Links delegated to broad principals",
            CategoryName = "Active Roles Configuration",
            KpiKey = "BroadDelegationLinks",
            Severity = AssessmentSeverity.High,
            Types = ActiveRolesGdpr,
            WarnThreshold = 1, FailThreshold = 1,
            Recommendation = "Access Template Links assigned to Everyone, Authenticated Users or Domain Users grant delegated administrative rights to the entire population. Re-scope these links to specific least-privilege groups."
        },
        new()
        {
            Id = "AR-ConfigDatabaseResilience",
            Title = "Configuration database is a single point of failure",
            CategoryName = "Active Roles Configuration",
            KpiKey = "ConfigDatabases",
            Severity = AssessmentSeverity.High,
            Types = ActiveRolesDora,
            Comparison = AssessmentComparison.AtMost,
            WarnThreshold = 1, FailThreshold = 1,
            Recommendation = "Only one Active Roles configuration database is present, making it a single point of failure. Add additional configuration databases and configure SQL replication so the configuration remains available if one database fails."
        },
        new()
        {
            Id = "AR-HistoryDatabaseResilience",
            Title = "Management History database is a single point of failure",
            CategoryName = "Active Roles Configuration",
            KpiKey = "HistoryDatabases",
            Severity = AssessmentSeverity.Medium,
            Types = ActiveRolesDora,
            Comparison = AssessmentComparison.AtMost,
            WarnThreshold = 1, FailThreshold = 1,
            Recommendation = "Only one Active Roles Management History database is present, making it a single point of failure. Add additional history databases and configure replication so change-history data remains available if one database fails."
        },

        // --- Additional AD security coverage ---------------------------------
        new()
        {
            Id = "SEC-TrustedForDelegation",
            Title = "Accounts trusted for unconstrained delegation",
            CategoryName = "Delegation",
            KpiKey = "TrustedForDelegation",
            Severity = AssessmentSeverity.Critical,
            Types = ComplianceGdpr,
            WarnThreshold = 1, FailThreshold = 1,
            Recommendation = "Accounts trusted for unconstrained delegation can impersonate any user that authenticates to them, a common lateral-movement path. Replace with constrained delegation or resource-based constrained delegation."
        },
        new()
        {
            Id = "SEC-NoKerberosPreauth",
            Title = "Accounts without Kerberos pre-authentication",
            CategoryName = "Account Security",
            KpiKey = "NoKerberosPreauth",
            Severity = AssessmentSeverity.High,
            Types = Compliance,
            WarnThreshold = 1, FailThreshold = 1,
            Recommendation = "Accounts with Kerberos pre-authentication disabled are vulnerable to AS-REP roasting (offline password cracking). Re-enable pre-authentication on these accounts."
        },
        new()
        {
            Id = "SEC-UseDesEncryption",
            Title = "Accounts configured to use DES encryption",
            CategoryName = "Weak Cryptography",
            KpiKey = "UseDesEncryption",
            Severity = AssessmentSeverity.High,
            Types = Compliance,
            WarnThreshold = 1, FailThreshold = 1,
            Recommendation = "DES is a broken cipher. Remove the DES-only flag so accounts negotiate AES Kerberos encryption."
        },
        new()
        {
            Id = "SEC-PasswordNotRequired",
            Title = "Accounts where a password is not required",
            CategoryName = "Account Security",
            KpiKey = "PasswordNotRequired",
            Severity = AssessmentSeverity.High,
            Types = ComplianceGdpr,
            WarnThreshold = 1, FailThreshold = 1,
            Recommendation = "Accounts flagged PASSWD_NOTREQD can have a blank password. Clear this flag and enforce the domain password policy on these accounts."
        },
        new()
        {
            Id = "SEC-UnconstrainedComputers",
            Title = "Computers trusted for unconstrained delegation",
            CategoryName = "Delegation",
            KpiKey = "UnconstrainedComputers",
            Severity = AssessmentSeverity.Critical,
            Types = Compliance,
            WarnThreshold = 1, FailThreshold = 1,
            Recommendation = "Computers with unconstrained delegation cache TGTs of every user that connects, enabling credential theft if the host is compromised. Migrate to constrained or resource-based constrained delegation."
        },

        // --- GDPR-specific accountability rules -------------------------------
        // These rules exist only under the GDPR lens (Art. 32 security of processing /
        // Art. 5(2) & 24 accountability) and reuse existing directory telemetry. They do
        // not assert compliance; they highlight access-governance weaknesses that would
        // undermine demonstrable security of personal-data processing.
        new()
        {
            Id = "GDPR-CannotChangePassword",
            Title = "Users who cannot change their own password",
            CategoryName = "GDPR Accountability",
            KpiKey = "CannotChangePassword",
            Severity = AssessmentSeverity.Medium,
            Types = GdprOnly,
            WarnThreshold = 10, FailThreshold = 50,
            Recommendation = "Accounts that cannot change their own password prevent timely credential rotation after a suspected compromise, weakening the security of processing expected under GDPR Art. 32. Restrict this flag to approved service accounts."
        },
        new()
        {
            Id = "GDPR-DeprovisionedResidue",
            Title = "Deprovisioned accounts retaining directory access",
            CategoryName = "GDPR Accountability",
            KpiKey = "DeprovisionedUsers",
            Severity = AssessmentSeverity.Medium,
            Types = GdprOnly,
            WarnThreshold = 10, FailThreshold = 50,
            Recommendation = "Accounts left in a deprovisioned state can retain residual access to systems processing personal data. Complete the joiner-mover-leaver lifecycle to support storage-limitation and access-governance accountability under GDPR Art. 5(2) and 24."
        },

        // --- DORA-specific ICT protection & resilience rules ------------------
        // These rules exist only under the DORA lens (Regulation (EU) 2022/2554),
        // focusing on Article 9 protection/prevention controls and resilience of the
        // administration control plane. They reuse existing directory telemetry and do
        // not assert compliance; they highlight weaknesses in ICT access protection and
        // operational resilience. Inverted (AtMost) rules flag insufficient adoption.
        new()
        {
            Id = "DORA-StrongAuthAdoption",
            Title = "Accounts enforcing strong (smart-card) authentication",
            CategoryName = "DORA Resilience",
            KpiKey = "SmartCardRequired",
            Severity = AssessmentSeverity.Medium,
            Types = DoraOnly,
            Comparison = AssessmentComparison.AtMost,
            WarnThreshold = 2, FailThreshold = 0,
            Recommendation = "DORA ICT protection (Art. 9) expects strong authentication and access controls to protect the confidentiality and integrity of information assets. Enforce smart-card / phishing-resistant MFA, prioritising administrative and high-privilege accounts; a count of zero indicates strong authentication is not enforced anywhere."
        },
        new()
        {
            Id = "DORA-DelegationProtectionAdoption",
            Title = "Privileged accounts protected from delegation",
            CategoryName = "DORA Resilience",
            KpiKey = "SensitiveCannotDelegate",
            Severity = AssessmentSeverity.Medium,
            Types = DoraOnly,
            Comparison = AssessmentComparison.AtMost,
            WarnThreshold = 2, FailThreshold = 0,
            Recommendation = "Protecting privileged accounts from credential delegation limits the blast radius of a compromise, supporting ICT resilience and least-privilege access controls under DORA Art. 9. Mark administrative accounts as 'sensitive and cannot be delegated' or add them to the Protected Users group; a count of zero indicates this protection is not applied anywhere."
        },

        // --- HIPAA-specific workforce access-management rules -----------------
        // These rules exist only under the HIPAA lens and focus on the Security Rule
        // workforce access-management and access-control safeguards (45 CFR 164.308(a)(3)-(4)
        // and 164.312(a),(d)). They reuse existing directory telemetry, do not scope ePHI,
        // and do not assert compliance.
        new()
        {
            Id = "HIPAA-CannotChangePassword",
            Title = "Users who cannot change their own password",
            CategoryName = "Authentication & Credential Lifecycle",
            KpiKey = "CannotChangePassword",
            Severity = AssessmentSeverity.Medium,
            Types = CredentialLifecycleShared,
            WarnThreshold = 10, FailThreshold = 50,
            Recommendation = "Accounts that cannot change their own password prevent timely credential rotation after a suspected compromise, weakening the person/entity authentication and access controls expected under the HIPAA Security Rule (45 CFR 164.312(a),(d)), the user authentication and credential-management controls of PCI DSS Requirement 8 (notably 8.3), and NIST SP 800-171 Identification and Authentication (3.5). Restrict this flag to approved service accounts."
        },
        new()
        {
            Id = "HIPAA-ProvisioningBacklog",
            Title = "Accounts flagged to change password at next logon",
            CategoryName = "Authentication & Credential Lifecycle",
            KpiKey = "MustChangePassword",
            Severity = AssessmentSeverity.Info,
            Types = CredentialLifecycleShared,
            WarnThreshold = 25, FailThreshold = 100,
            Recommendation = "A large backlog of 'must change password at next logon' accounts can indicate stalled onboarding or dormant provisioning, which undermines disciplined workforce access establishment and modification under HIPAA 45 CFR 164.308(a)(3)-(4), the account provisioning and lifecycle expectations of PCI DSS Requirement 8 (8.1-8.2), and NIST SP 800-171 account management under Access Control (3.1) and Identification and Authentication (3.5). Review and follow up on these accounts."
        },

        // --- SOX-specific ITGC (access & change management) rules -------------
        // These rules exist only under the SOX lens and focus on IT General Controls over
        // 'access to programs and data' and 'change management' as commonly framed under
        // COBIT/COSO. They reuse existing directory telemetry as segregation-of-duties and
        // least-privilege indicators; they do not scope financially relevant systems and do
        // not assert controls effectiveness or compliance.
        new()
        {
            Id = "SOX-AccountOperators",
            Title = "Account Operators membership (SoD / privileged access indicator)",
            CategoryName = "SOX IT General Controls",
            KpiKey = "AccountOperators",
            Severity = AssessmentSeverity.High,
            Types = SoxOnly,
            WarnThreshold = 1, FailThreshold = 3,
            Recommendation = "Account Operators is a powerful legacy group that can manage most user and group objects, creating segregation-of-duties and least-privilege concerns for ITGC 'access to programs and data'. Keep it empty and delegate specific, reviewable permissions instead."
        },
        new()
        {
            Id = "SOX-ServerOperators",
            Title = "Server Operators membership (SoD / privileged access indicator)",
            CategoryName = "SOX IT General Controls",
            KpiKey = "ServerOperators",
            Severity = AssessmentSeverity.High,
            Types = SoxOnly,
            WarnThreshold = 1, FailThreshold = 3,
            Recommendation = "Server Operators can sign in to and control domain controllers, a concentration of privilege that weakens segregation of duties over financially relevant infrastructure. Keep this legacy group empty and use targeted, reviewable delegation."
        },

        // --- TSA-specific management-plane privileged-access rules ------------
        // These rules exist only under the TSA lens and focus on protecting the identity
        // and administration (management) plane, reflecting the privileged-access and
        // access-control themes of the UK Telecommunications Security Act 2021 Code of
        // Practice. Inverted (AtMost) rules flag insufficient adoption. They do not scope
        // security-critical functions or network equipment and do not assert compliance.
        new()
        {
            Id = "TSA-PrivilegedStrongAuth",
            Title = "Management-plane accounts enforcing strong (smart-card) authentication",
            CategoryName = "TSA Management Plane",
            KpiKey = "SmartCardRequired",
            Severity = AssessmentSeverity.High,
            Types = TsaOnly,
            Comparison = AssessmentComparison.AtMost,
            WarnThreshold = 2, FailThreshold = 0,
            Recommendation = "The TSA Code of Practice expects privileged access to the management plane to be strongly protected. Enforce smart-card / phishing-resistant MFA for administrative and high-privilege accounts that manage the identity and network administration plane; a count of zero indicates strong authentication is not enforced anywhere."
        },
        new()
        {
            Id = "TSA-PrivilegedDelegationProtection",
            Title = "Management-plane privileged accounts protected from delegation",
            CategoryName = "TSA Management Plane",
            KpiKey = "SensitiveCannotDelegate",
            Severity = AssessmentSeverity.Medium,
            Types = TsaOnly,
            Comparison = AssessmentComparison.AtMost,
            WarnThreshold = 2, FailThreshold = 0,
            Recommendation = "Protecting privileged administration-plane accounts from credential delegation limits lateral movement into security-relevant management systems, supporting the TSA privileged-access protection expectations. Mark administrative accounts as 'sensitive and cannot be delegated' or add them to the Protected Users group; a count of zero indicates this protection is not applied anywhere."
        },

        // --- CAF-specific Principle B2 (Identity & Access Control) rules ------
        // These rules exist only under the CAF lens and provide supporting indicators for
        // NCSC CAF Principle B2 (Identity and Access Control). Inverted (AtMost) rules flag
        // insufficient adoption. They do not produce CAF outcome ratings and do not cover
        // other CAF objectives or principles.
        new()
        {
            Id = "CAF-B2-StrongAuthAdoption",
            Title = "Accounts enforcing strong (smart-card) authentication",
            CategoryName = "CAF B2 Identity & Access Control",
            KpiKey = "SmartCardRequired",
            Severity = AssessmentSeverity.High,
            Types = CafOnly,
            Comparison = AssessmentComparison.AtMost,
            WarnThreshold = 2, FailThreshold = 0,
            Recommendation = "CAF Principle B2 expects robust identity verification and authentication, especially for privileged access. Enforce smart-card / phishing-resistant MFA, prioritising administrative and high-privilege accounts; a count of zero indicates strong authentication is not enforced anywhere."
        },
        new()
        {
            Id = "CAF-B2-PrivilegedDelegationProtection",
            Title = "Privileged accounts protected from delegation",
            CategoryName = "CAF B2 Identity & Access Control",
            KpiKey = "SensitiveCannotDelegate",
            Severity = AssessmentSeverity.Medium,
            Types = CafOnly,
            Comparison = AssessmentComparison.AtMost,
            WarnThreshold = 2, FailThreshold = 0,
            Recommendation = "CAF Principle B2 expects privileged access to be tightly managed and protected. Mark administrative accounts as 'sensitive and cannot be delegated' or add them to the Protected Users group so their credentials cannot be forwarded via delegation; a count of zero indicates this protection is not applied anywhere."
        },

        // --- Cyber Essentials-specific User Access Control rules --------------
        // These rules exist only under the Cyber Essentials lens and provide supporting
        // indicators for the 'User Access Control' technical control (and, in part, 'Secure
        // Configuration'). They do not produce a Cyber Essentials verdict and do not cover
        // firewalls, malware protection, or security update management.
        new()
        {
            Id = "CE-UAC-AdminAccountCount",
            Title = "Domain Admins membership size",
            CategoryName = "Cyber Essentials User Access Control",
            KpiKey = "DomainAdmins",
            Severity = AssessmentSeverity.High,
            Types = CyberEssentialsOnly,
            WarnThreshold = 5, FailThreshold = 10,
            Recommendation = "Cyber Essentials 'User Access Control' requires administrative accounts to be kept to a minimum and used only when necessary. Keep Domain Admins to a small, named set, remove standing administrative rights where they are not needed, and use separate accounts for administrative tasks."
        },
        new()
        {
            Id = "CE-UAC-StaleAccounts",
            Title = "Stale / inactive enabled accounts",
            CategoryName = "Cyber Essentials User Access Control",
            KpiKey = "StaleUsers",
            Severity = AssessmentSeverity.Medium,
            Types = CyberEssentialsOnly,
            WarnThreshold = 5, FailThreshold = 20,
            Recommendation = "Cyber Essentials 'User Access Control' requires user accounts to be removed or disabled when no longer required. Disable or deprovision accounts that have been inactive beyond your defined threshold, particularly for leavers, to reduce the attack surface."
        },

        // --- DSPT-specific NDG Standard 4 (Managing Data Access) rules --------
        // These rules exist only under the DSPT lens and provide supporting indicators for the
        // National Data Guardian 'Managing Data Access' standard (Standard 4). They do not
        // produce a DSPT status and do not cover training, policy, incident, continuity or
        // supplier assertions.
        new()
        {
            Id = "DSPT-S4-PrivilegedAccessMinimisation",
            Title = "Domain Admins membership size",
            CategoryName = "DSPT Managing Data Access",
            KpiKey = "DomainAdmins",
            Severity = AssessmentSeverity.High,
            Types = DsptOnly,
            WarnThreshold = 5, FailThreshold = 10,
            Recommendation = "DSPT NDG Standard 4 (Managing Data Access) expects access to be limited to those who need it. Keep Domain Admins to a small, named set of accounts, remove standing membership, and review privileged access regularly."
        },
        new()
        {
            Id = "DSPT-S4-LeaverStaleAccounts",
            Title = "Stale / inactive enabled accounts",
            CategoryName = "DSPT Managing Data Access",
            KpiKey = "StaleUsers",
            Severity = AssessmentSeverity.Medium,
            Types = DsptOnly,
            WarnThreshold = 5, FailThreshold = 20,
            Recommendation = "DSPT NDG Standard 4 (Managing Data Access) expects access to be removed promptly when no longer needed. Disable or deprovision accounts that have been inactive beyond your defined threshold, particularly for leavers, so access to personal confidential data is not retained unnecessarily."
        },

        // --- DfE-specific account & access management standard rules ----------
        // These rules exist only under the DfE Cyber Standards lens and provide supporting
        // indicators for the account and access management standards (least privilege,
        // administrator account use, strong authentication, account lifecycle). They do not
        // produce a DfE standards-met determination and do not cover backups, boundary
        // firewalls, anti-malware, patching, training or incident response.
        new()
        {
            Id = "DFE-AAM-LeastPrivilege",
            Title = "Domain Admins membership size",
            CategoryName = "DfE Account & Access Management",
            KpiKey = "DomainAdmins",
            Severity = AssessmentSeverity.High,
            Types = DfeCyberOnly,
            WarnThreshold = 5, FailThreshold = 10,
            Recommendation = "The DfE cyber standards expect accounts to use the least amount of access needed and administrator accounts to be used only for admin tasks. Keep Domain Admins to a small, named set, remove standing membership, and use separate accounts for administrative work."
        },
        new()
        {
            Id = "DFE-AAM-StrongAuthAdoption",
            Title = "Accounts enforcing strong (smart-card) authentication",
            CategoryName = "DfE Account & Access Management",
            KpiKey = "SmartCardRequired",
            Severity = AssessmentSeverity.High,
            Types = DfeCyberOnly,
            Comparison = AssessmentComparison.AtMost,
            WarnThreshold = 2, FailThreshold = 0,
            Recommendation = "The DfE cyber standards expect multi-factor authentication for accounts with access to sensitive or personal data, and especially for administrators. Enforce smart-card / phishing-resistant MFA, prioritising administrative and high-privilege accounts; a count of zero indicates strong authentication is not enforced anywhere."
        },
        new()
        {
            Id = "DFE-AAM-LeaverStaleAccounts",
            Title = "Stale / inactive enabled accounts",
            CategoryName = "DfE Account & Access Management",
            KpiKey = "StaleUsers",
            Severity = AssessmentSeverity.Medium,
            Types = DfeCyberOnly,
            WarnThreshold = 5, FailThreshold = 20,
            Recommendation = "The DfE cyber standards expect accounts to be removed or disabled when no longer required. Disable or deprovision accounts that have been inactive beyond your defined threshold, particularly for leavers, to reduce the attack surface."
        }
    };
    /// <summary>Returns the rules applicable to the given assessment type.</summary>
    public static IEnumerable<AssessmentRule> ForType(AssessmentType type) =>
        All.Where(r => r.Types.Contains(type));

    /// <summary>
    /// True when the assessment type evaluates at least one rule that depends on Entra group
    /// membership (see <see cref="KpiInfo.EntraMembershipDependentKeys"/>). Such assessments
    /// (e.g. Entra ID, SOX) must not be run until lazy membership loading has completed, or their
    /// group-membership checks would score against provisional (zero) counts. Types with no such
    /// rules (e.g. Active Roles, Active Directory) are unaffected and can run immediately.
    /// </summary>
    public static bool DependsOnEntraGroupMembership(AssessmentType type) =>
        type == AssessmentType.Entra ||
        ForType(type).Any(r => KpiInfo.EntraMembershipDependentKeys.Contains(r.KpiKey));
}

// ---------------------------------------------------------------------------
// Result types
// ---------------------------------------------------------------------------

public class AssessmentCheck
{
    public string RuleId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public AssessmentSeverity Severity { get; set; }
    public AssessmentStatus Status { get; set; }
    public int Count { get; set; }
    public string? Error { get; set; }
    public string Recommendation { get; set; } = string.Empty;
}

public class AssessmentCategory
{
    public string Name { get; set; } = string.Empty;
    public List<AssessmentCheck> Checks { get; set; } = new();

    public int FailCount => Checks.Count(c => c.Status == AssessmentStatus.Fail);
    public int WarnCount => Checks.Count(c => c.Status == AssessmentStatus.Warning);
    public int PassCount => Checks.Count(c => c.Status == AssessmentStatus.Pass);
}

public class AssessmentResult
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public AssessmentType Type { get; set; } = AssessmentType.ActiveDirectory;
    public string? Label { get; set; }
    public string? GeneratedBy { get; set; }
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public List<AssessmentCategory> Categories { get; set; } = new();

    public int TotalChecks { get; set; }
    public int PassCount { get; set; }
    public int WarnCount { get; set; }
    public int FailCount { get; set; }
    public int NotApplicableCount { get; set; }

    /// <summary>Weighted score from 0-100 (higher is better).</summary>
    public int Score { get; set; }

    /// <summary>Letter grade derived from <see cref="Score"/>.</summary>
    public string Grade { get; set; } = "-";
}

/// <summary>Lightweight descriptor for listing stored assessments without loading the full body.</summary>
public class AssessmentHeader
{
    public string Id { get; set; } = string.Empty;
    public AssessmentType Type { get; set; }
    public string? Label { get; set; }
    public string? GeneratedBy { get; set; }
    public DateTime GeneratedUtc { get; set; }
    public int Score { get; set; }
    public string Grade { get; set; } = "-";
    public int FailCount { get; set; }
    public int WarnCount { get; set; }
    public int PassCount { get; set; }
}

// ---------------------------------------------------------------------------
// Comparison result types (compare two assessment runs of the same type)
// ---------------------------------------------------------------------------

/// <summary>
/// How a single check changed between the baseline ("from") run and the
/// compared ("to") run. Note this is distinct from <see cref="AssessmentComparison"/>,
/// which describes a rule's threshold direction.
/// </summary>
public enum CheckDelta
{
    Unchanged,
    Improved,   // status moved to a better outcome (e.g. Fail -> Pass)
    Worsened,   // status moved to a worse outcome (e.g. Pass -> Fail)
    Added,      // check present in the "to" run only
    Removed     // check present in the "from" run only
}

/// <summary>How a change should be interpreted for colouring: good, bad, or neutral.</summary>
public enum CheckDeltaSentiment
{
    Neutral,
    Good,
    Bad
}

/// <summary>The full result of comparing a baseline assessment run against another saved run or the current live evaluation.</summary>
public class AssessmentRunComparison
{
    public AssessmentHeader From { get; set; } = new();
    public AssessmentHeader To { get; set; } = new();

    /// <summary>True when the "To" side represents a live (unsaved) evaluation rather than a saved run.</summary>
    public bool ToIsCurrent { get; set; }

    public AssessmentType Type { get; set; }

    public List<AssessmentComparisonCategory> Categories { get; set; } = new();

    // Headline deltas
    public int ScoreChange { get; set; }
    public int FailChange { get; set; }
    public int WarnChange { get; set; }
    public int PassChange { get; set; }

    // Per-check transition tallies
    public int ImprovedCount { get; set; }
    public int WorsenedCount { get; set; }
    public int UnchangedCount { get; set; }
    public int AddedCount { get; set; }
    public int RemovedCount { get; set; }
}

public class AssessmentComparisonCategory
{
    public string Name { get; set; } = string.Empty;
    public List<AssessmentComparisonRow> Rows { get; set; } = new();
}

public class AssessmentComparisonRow
{
    public string RuleId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public AssessmentSeverity Severity { get; set; }

    public AssessmentStatus? FromStatus { get; set; }
    public AssessmentStatus? ToStatus { get; set; }
    public int? FromCount { get; set; }
    public int? ToCount { get; set; }

    public CheckDelta Delta { get; set; }
    public CheckDeltaSentiment Sentiment { get; set; }
}
