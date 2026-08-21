# Active Roles Dashboard — Assessments Reference

[← Back to README](../README.md)

This document describes the security/compliance assessments

The assessment engine is **metadata-driven**. Every rule targets a single **KPI key**, reads that KPI's count from the collected dashboard telemetry, and compares it against **warn** and **fail** thresholds. An assessment "type" (framework) simply selects the subset of rules that apply to it.

> Source of truth: [`Models/AssessmentModels.cs`](../Models/AssessmentModels.cs) (rule library and types) and [`Models/DashboardModels.cs`](../Models/DashboardModels.cs) (KPI definitions and `GetKpiResult` mapping). Data collection lives in [`Services/ActiveRolesService.cs`](../Services/ActiveRolesService.cs).

---

## 1. How the engine works

### Rule evaluation

Each rule (`AssessmentRule`) has:

| Field | Meaning |
|-------|---------|
| `Id` | Stable rule identifier (e.g. `PRIV-DomainAdmins`). |
| `Title` | Human-readable rule name shown in the UI/report. |
| `CategoryName` | Grouping heading in the scorecard (e.g. *Privileged Access*). |
| `KpiKey` | The KPI whose count the rule evaluates. |
| `Severity` | `Info` \| `Low` \| `Medium` \| `High` \| `Critical` — drives scoring weight and ordering. |
| `Types` | Which assessment frameworks the rule applies to. |
| `Comparison` | `AtLeast` (default) or `AtMost`. |
| `WarnThreshold` / `FailThreshold` | Count boundaries that determine the outcome. |
| `Recommendation` | Guidance shown when the rule does not pass. |

### Comparison direction

- **`AtLeast` (default)** — a **higher** count is worse. A count `>= FailThreshold` **fails**; `>= WarnThreshold` **warns**; otherwise **passes**. Used for risk indicators (e.g. accounts with reversible encryption, oversized privileged groups).
- **`AtMost` (inverted)** — a **lower** count is worse. A count `<= FailThreshold` **fails**; `<= WarnThreshold` **warns**; otherwise **passes**. Used for adoption/coverage/resilience rules where *more is better* (e.g. accounts enforcing smart-card MFA, number of configuration databases).

### Outcomes and scoring

- Outcomes: **Pass**, **Warning**, **Fail**, or **Not Applicable** (KPI errored or data unavailable).
- Results are grouped into categories; each assessment produces a weighted **Score (0–100)** and a **Grade**, based on rule severity and outcome.

### Saved runs, history and compare

- Each run is saved to history and can be re-viewed, exported (PDF/Word), or deleted.
- Two saved runs **of the same type** can be compared side-by-side, or a saved baseline can be compared against a fresh **current (live)** evaluation. The comparison shows per-check status transitions (e.g. Fail → Pass), score/grade movement, and checks that were added or removed between runs.
- Comparing a membership-dependent type against *current* honors the same Entra-membership gate as running one, so a live comparison is never computed against provisional data.

---

## 2. Assessment types (frameworks)

Every framework is honestly **scoped**: it evaluates only the identity/access and configuration controls that AD + Active Roles telemetry can evidence, and is **not** a compliance determination. Frameworks marked *(scope banner)* show an explicit scope/disclaimer statement in the UI and exported reports.

| Assessment Type | Display Name | Scope banner |
|-----------------|--------------|:------------:|
| `ActiveDirectory` | Active Directory | — |
| `Entra` | Entra ID | ✔ |
| `Nis2` | NIS2 | — |
| `Cis` | CIS Benchmark | — |
| `Nist` | NIST CSF (Identity & Access) | — |
| `Nist171` | NIST SP 800-171 (Access Control & Identification/Authentication - 3.1 & 3.5) | ✔ |
| `Nen7510` | NEN 7510 (Identity & Access) | — |
| `Iso27001` | ISO/IEC 27001 (Access Control) | — |
| `Gdpr` | GDPR (Art. 32 Security of Processing) | ✔ |
| `Dora` | DORA (ICT Access Controls & Resilience) | ✔ |
| `Hipaa` | HIPAA (Security Rule Access Controls) | ✔ |
| `Sox` | SOX (IT General Controls - Access & Change) | ✔ |
| `Tsa` | TSA (Management-Plane Access & Privilege) | ✔ |
| `Caf` | CAF (Identity & Access Control - Principle B2) | ✔ |
| `CyberEssentials` | Cyber Essentials (User Access Control) | ✔ |
| `Dspt` | DSPT (Managing Data Access - NDG Standard 4) | ✔ |
| `DfeCyber` | DfE Cyber Standards (Account & Access Management) | ✔ |
| `PciDss` | PCI DSS (Requirements 7 & 8 - Access Control) | ✔ |
| `ActiveRoles` | Active Roles Configuration | — |

### Rule groupings (which frameworks share rules)

Rules are assigned to reusable framework groupings so a control can be defined once and reused:

| Grouping | Member frameworks |
|----------|-------------------|
| `Compliance` | AD, NIS2, CIS, NIST, NEN 7510, ISO 27001 |
| `ComplianceNoCis` | AD, NIS2, NIST, NEN 7510, ISO 27001 |
| `ComplianceGdpr` | `Compliance` + GDPR, DORA, HIPAA, SOX, TSA, CAF, Cyber Essentials, DSPT, DfE, PCI DSS, 800-171 |
| `ComplianceNoCisGdpr` | `ComplianceNoCis` + GDPR, DORA, HIPAA, SOX, TSA, CAF, Cyber Essentials, DSPT, DfE, PCI DSS, 800-171 |
| `SecureConfigUnsupportedOs` | AD, NIS2, CIS, NIST, NEN 7510, ISO 27001, Cyber Essentials, DfE, DSPT, PCI DSS, 800-171 |
| `SecureConfigCore` | `SecureConfigUnsupportedOs` + DORA, HIPAA, SOX (also PCI DSS, 800-171) |
| `EntraGovernance` | Entra, NIS2, CIS, NIST, NEN 7510, ISO 27001, GDPR, DORA, HIPAA, SOX, TSA, CAF, Cyber Essentials, DSPT, DfE, PCI DSS, 800-171 (**AD excluded** — it has its own AD-scoped group/user rules) |
| `ActiveRolesGdpr` | Active Roles + GDPR, DORA, HIPAA, SOX, TSA, CAF, Cyber Essentials, DSPT, DfE, PCI DSS, 800-171 |
| `ActiveRolesDora` | Active Roles, DORA |
| `AdOnly` | AD only |
| `ActiveRolesOnly` | Active Roles only |
| `CredentialLifecycleShared` | HIPAA + PCI DSS + NIST 800-171 (shared password/authentication-lifecycle rules) |
| `GdprOnly` / `DoraOnly` / `HipaaOnly` / `SoxOnly` / `TsaOnly` / `CafOnly` / `CyberEssentialsOnly` / `DsptOnly` / `DfeCyberOnly` | Single-framework rules |

---

## 3. Rules

Thresholds are shown as `warn / fail`. Unless noted, comparison is **AtLeast** (higher = worse). Rules marked **↓ AtMost** are inverted (lower = worse).

### Account Security

| Rule Id | Title | KPI Key | Severity | Warn/Fail | Applies to |
|---------|-------|---------|----------|-----------|-----------|
| `SEC-ReversibleEncryption` | Users with reversible password encryption | `ReversibleEncryption` | Critical | 1 / 1 | Compliance |
| `SEC-PasswordNotRequired` | Users where a password is not required | `PasswordNotRequired` | Critical | 1 / 1 | Compliance |
| `SEC-NoKerberosPreauth` | Accounts not requiring Kerberos pre-authentication | `NoKerberosPreauth` | High | 1 / 1 | Compliance |
| `SEC-UseDesEncryption` | Accounts using DES encryption | `UseDesEncryption` | High | 1 / 1 | Compliance |
| `SEC-TrustedForDelegation` | Accounts trusted for unconstrained delegation | `TrustedForDelegation` | High | 1 / 5 | Compliance |
| `SEC-SpnUserAccounts` | User accounts with an SPN (Kerberoastable) | `SpnUserAccounts` | High | 1 / 10 | Compliance |
| `SEC-UnconstrainedComputers` | Computers configured for unconstrained delegation | `UnconstrainedComputers` | Critical | 1 / 1 | ComplianceGdpr |
| `SEC-PasswordNeverExpires` | Users whose password never expires | `PasswordNeverExpires` | Medium | 5 / 25 | ComplianceGdpr |
| `SEC-CannotChangePassword` | Users who cannot change their own password | `CannotChangePassword` | Low | 10 / 50 | ComplianceNoCis |
| `SEC-MustChangePassword` | Users flagged to change password at next logon | `MustChangePassword` | Info | 25 / 100 | AdOnly |
| `SEC-NoKerberosPreauth` (coverage) | Accounts without Kerberos pre-authentication | `NoKerberosPreauth` | High | 1 / 1 | Compliance |
| `SEC-UseDesEncryption` (coverage) | Accounts configured to use DES encryption | `UseDesEncryption` | High | 1 / 1 | Compliance |
| `SEC-PasswordNotRequired` (coverage) | Accounts where a password is not required | `PasswordNotRequired` | High | 1 / 1 | ComplianceGdpr |
| `SEC-TrustedForDelegation` (delegation) | Accounts trusted for unconstrained delegation | `TrustedForDelegation` | Critical | 1 / 1 | ComplianceGdpr |
| `SEC-UnconstrainedComputers` (delegation) | Computers trusted for unconstrained delegation | `UnconstrainedComputers` | Critical | 1 / 1 | Compliance |

### Delegation Protection *(inverted)*

| Rule Id | Title | KPI Key | Severity | Warn/Fail | Applies to |
|---------|-------|---------|----------|-----------|-----------|
| `SEC-SensitiveCannotDelegateAdoption` ↓ | Privileged accounts protected from delegation | `SensitiveCannotDelegate` | Medium | 2 / 0 | NIS2, NIST, NEN 7510, ISO 27001 |

### Account Hygiene

| Rule Id | Title | KPI Key | Severity | Warn/Fail | Applies to |
|---------|-------|---------|----------|-----------|-----------|
| `HYG-ExpiredUsers` | Expired user accounts still present | `ExpiredUsers` | Medium | 1 / 10 | ComplianceNoCis |
| `HYG-NeverLoggedIn` | Accounts that have never logged in | `NeverLoggedIn` | Low | 5 / 25 | ComplianceNoCisGdpr |
| `HYG-UserAccountLockedOut` | Currently locked-out user accounts | `UserAccountLockedOut` | Low | 5 / 20 | AdOnly |
| `HYG-NoManagerUser` | User accounts without a manager | `NoManagerUser` | Low | 10 / 50 | AdOnly |
| `HYG-NoManagerServiceAccount` | Service accounts without a manager | `NoManagerServiceAccount` | Medium | 1 / 10 | ComplianceNoCis |
| `HYG-StaleAccounts` | Stale (inactive) enabled accounts | `StaleUsers` | High | 5 / 25 | ComplianceGdpr |
| `HYG-DeprovisionedUsers` | Deprovisioned accounts still present | `DeprovisionedUsers` | Medium | 10 / 50 | ComplianceNoCisGdpr |

### Group Governance

| Rule Id | Title | KPI Key | Severity | Warn/Fail | Applies to |
|---------|-------|---------|----------|-----------|-----------|
| `GRP-NoGroupOwner` | Groups without an owner | `NoGroupOwner` | Medium | 5 / 25 | AdOnly |
| `GRP-EmptyGroups` | Empty groups | `EmptyGroups` | Low | 10 / 50 | AdOnly |

### Entra Identity Governance

Entra ID group-ownership/review and guest/external-access indicators surfaced through Active Roles. These rules feed the dedicated **Entra ID** assessment and every compliance framework that already carries user/group governance rules (the `EntraGovernance` grouping). Active Directory is intentionally excluded because it has its own AD-scoped group/user rules.

| Rule Id | Title | KPI Key | Severity | Warn/Fail | Applies to |
|---------|-------|---------|----------|-----------|-----------|
| `ENT-NoGroupOwner` | Entra groups without an owner | `EntraNoGroupOwner` | Medium | 5 / 25 | EntraGovernance |
| `ENT-GuestContainingGroups` | Entra groups containing guest (external) members | `EntraGuestContainingGroups` | High | 5 / 25 | EntraGovernance |
| `ENT-SingleOwnerGroups` | Entra groups with only a single owner | `EntraSingleOwnerGroups` | Low | 10 / 50 | EntraGovernance |
| `ENT-GuestUsers` | Entra guest (external) user accounts | `EntraGuestUsers` | Medium | 25 / 100 | EntraGovernance |
| `ENT-NoManagerUser` | Entra user accounts without a manager | `EntraNoManagerUser` | Low | 10 / 50 | EntraGovernance |

> **Membership-dependent rules:** `ENT-NoGroupOwner`, `ENT-GuestContainingGroups` and `ENT-SingleOwnerGroups` read Entra **group** KPIs that require lazily loaded Entra group membership to be fully populated first. Any assessment type that evaluates one of these (and the Entra ID assessment always) is blocked from running until membership loading completes — otherwise the group checks would score against provisional (zero) counts. This gate is enforced by `AssessmentRuleLibrary.DependsOnEntraGroupMembership(type)`. The `ENT-GuestUsers` and `ENT-NoManagerUser` rules resolve from eagerly loaded Entra user data and are not gated.

### Secure Configuration

| Rule Id | Title | KPI Key | Severity | Warn/Fail | Applies to |
|---------|-------|---------|----------|-----------|-----------|
| `CFG-UnsupportedServerOs` | Servers running unsupported operating systems | `UnsupportedServerOs` | High | 1 / 5 | SecureConfigUnsupportedOs |
| `CFG-UnsupportedClientOs` | Clients running unsupported operating systems | `UnsupportedClientOs` | High | 1 / 10 | SecureConfigUnsupportedOs |
| `CFG-StaleComputers` | Stale (inactive) computer accounts | `StaleComputers` | Medium | 5 / 25 | SecureConfigUnsupportedOs |
| `CFG-KrbtgtPasswordAge` | krbtgt password age (days) | `KrbtgtPasswordAgeDays` | High | 180 / 365 | SecureConfigCore |
| `CFG-WeakPasswordLength` | Minimum password length below 12 characters | `WeakPasswordLength` | High | 1 / 1 | SecureConfigCore |
| `CFG-PasswordComplexityDisabled` | Password complexity disabled | `PasswordComplexityDisabled` | High | 1 / 1 | SecureConfigCore |
| `CFG-NoAccountLockout` | No account lockout threshold configured | `NoAccountLockout` | Medium | 1 / 1 | SecureConfigCore |
| `CFG-PasswordMaxAge` | Maximum password age (days) | `PasswordMaxAgeDays` | Low | 366 / 731 | SecureConfigCore |

> **Note on encoded signals:** `WeakPasswordLength`, `PasswordComplexityDisabled` and `NoAccountLockout` are **0/1 weakness indicators** (`1` = weak/misconfigured). `KrbtgtPasswordAgeDays` and `PasswordMaxAgeDays` are measured in **days**.

### Privileged Access (least privilege)

| Rule Id | Title | KPI Key | Severity | Warn/Fail | Applies to |
|---------|-------|---------|----------|-----------|-----------|
| `PRIV-AdminCountAccounts` | Accounts flagged with adminCount=1 | `AdminCount` | Medium | 15 / 40 | ComplianceGdpr |
| `PRIV-DomainAdmins` | Domain Admins membership size | `DomainAdmins` | High | 5 / 10 | ComplianceGdpr |
| `PRIV-EnterpriseAdmins` | Enterprise Admins membership size | `EnterpriseAdmins` | Critical | 2 / 5 | ComplianceGdpr |
| `PRIV-SchemaAdmins` | Schema Admins membership size | `SchemaAdmins` | High | 1 / 3 | Compliance |
| `PRIV-Administrators` | Built-in Administrators membership size | `Administrators` | High | 8 / 15 | ComplianceGdpr |
| `PRIV-AccountOperators` | Account Operators membership size | `AccountOperators` | High | 1 / 3 | Compliance |
| `PRIV-ServerOperators` | Server Operators membership size | `ServerOperators` | High | 1 / 3 | Compliance |
| `PRIV-BackupOperators` | Backup Operators membership size | `BackupOperators` | Medium | 2 / 5 | Compliance |

### Strong Authentication *(inverted)*

| Rule Id | Title | KPI Key | Severity | Warn/Fail | Applies to |
|---------|-------|---------|----------|-----------|-----------|
| `AUTH-SmartCardAdoption` ↓ | Accounts enforcing smart-card (strong) authentication | `SmartCardRequired` | Medium | 2 / 0 | NIS2, NIST, NEN 7510, ISO 27001 |

### Active Roles Configuration

| Rule Id | Title | KPI Key | Severity | Warn/Fail | Comparison | Applies to |
|---------|-------|---------|----------|-----------|-----------|-----------|
| `AR-DisabledWorkflows` | Disabled Active Roles workflows | `DisabledWorkflows` | Medium | 1 / 3 | AtLeast | ActiveRolesGdpr |
| `AR-BroadDelegationLinks` | Access Template Links delegated to broad principals | `BroadDelegationLinks` | High | 1 / 1 | AtLeast | ActiveRolesGdpr |
| `AR-ConfigDatabaseResilience` ↓ | Configuration database is a single point of failure | `ConfigDatabases` | High | 1 / 1 | AtMost | ActiveRolesDora |
| `AR-HistoryDatabaseResilience` ↓ | Management History database is a single point of failure | `HistoryDatabases` | Medium | 1 / 1 | AtMost | ActiveRolesDora |

### Framework-specific rules

These rules exist only under a single framework's lens and reuse existing telemetry.

| Rule Id | Title | KPI Key | Severity | Warn/Fail | Comparison | Framework |
|---------|-------|---------|----------|-----------|-----------|-----------|
| `GDPR-CannotChangePassword` | Users who cannot change their own password | `CannotChangePassword` | Medium | 10 / 50 | AtLeast | GDPR |
| `GDPR-DeprovisionedResidue` | Deprovisioned accounts retaining directory access | `DeprovisionedUsers` | Medium | 10 / 50 | AtLeast | GDPR |
| `DORA-StrongAuthAdoption` ↓ | Accounts enforcing strong (smart-card) authentication | `SmartCardRequired` | Medium | 2 / 0 | AtMost | DORA |
| `DORA-DelegationProtectionAdoption` ↓ | Privileged accounts protected from delegation | `SensitiveCannotDelegate` | Medium | 2 / 0 | AtMost | DORA |
| `HIPAA-CannotChangePassword` | Users who cannot change their own password | `CannotChangePassword` | Medium | 10 / 50 | AtLeast | HIPAA, PCI DSS, 800-171 |
| `HIPAA-ProvisioningBacklog` | Accounts flagged to change password at next logon | `MustChangePassword` | Info | 25 / 100 | AtLeast | HIPAA, PCI DSS, 800-171 |
| `SOX-AccountOperators` | Account Operators membership (SoD indicator) | `AccountOperators` | High | 1 / 3 | AtLeast | SOX |
| `SOX-ServerOperators` | Server Operators membership (SoD indicator) | `ServerOperators` | High | 1 / 3 | AtLeast | SOX |
| `TSA-PrivilegedStrongAuth` ↓ | Management-plane accounts enforcing strong auth | `SmartCardRequired` | High | 2 / 0 | AtMost | TSA |
| `TSA-PrivilegedDelegationProtection` ↓ | Management-plane privileged accounts protected from delegation | `SensitiveCannotDelegate` | Medium | 2 / 0 | AtMost | TSA |
| `CAF-B2-StrongAuthAdoption` ↓ | Accounts enforcing strong (smart-card) authentication | `SmartCardRequired` | High | 2 / 0 | AtMost | CAF |
| `CAF-B2-PrivilegedDelegationProtection` ↓ | Privileged accounts protected from delegation | `SensitiveCannotDelegate` | Medium | 2 / 0 | AtMost | CAF |
| `CE-UAC-AdminAccountCount` | Domain Admins membership size | `DomainAdmins` | High | 5 / 10 | AtLeast | Cyber Essentials |
| `CE-UAC-StaleAccounts` | Stale / inactive enabled accounts | `StaleUsers` | Medium | 5 / 20 | AtLeast | Cyber Essentials |
| `DSPT-S4-PrivilegedAccessMinimisation` | Domain Admins membership size | `DomainAdmins` | High | 5 / 10 | AtLeast | DSPT |
| `DSPT-S4-LeaverStaleAccounts` | Stale / inactive enabled accounts | `StaleUsers` | Medium | 5 / 20 | AtLeast | DSPT |
| `DFE-AAM-LeastPrivilege` | Domain Admins membership size | `DomainAdmins` | High | 5 / 10 | AtLeast | DfE Cyber |
| `DFE-AAM-StrongAuthAdoption` ↓ | Accounts enforcing strong (smart-card) authentication | `SmartCardRequired` | High | 2 / 0 | AtMost | DfE Cyber |
| `DFE-AAM-LeaverStaleAccounts` | Stale / inactive enabled accounts | `StaleUsers` | Medium | 5 / 20 | AtLeast | DfE Cyber |

---

## 4. KPI reference

Each rule reads a KPI count via `DashboardSummary.GetKpiResult(kpiKey)`. KPIs fall into three groups.

### Directory-object count KPIs

Counts of AD users/computers/groups matching a specific risky or hygiene condition:

`ReversibleEncryption`, `PasswordNotRequired`, `NoKerberosPreauth`, `UseDesEncryption`, `TrustedForDelegation`, `SpnUserAccounts`, `UnconstrainedComputers`, `PasswordNeverExpires`, `CannotChangePassword`, `MustChangePassword`, `SmartCardRequired`, `SensitiveCannotDelegate`, `ExpiredUsers`, `NeverLoggedIn`, `UserAccountLockedOut`, `NoManagerUser`, `NoManagerServiceAccount`, `StaleUsers`, `DeprovisionedUsers`, `NoGroupOwner`, `EmptyGroups`, `AdminCount`.

### Privileged-group membership KPIs

Membership sizes of built-in privileged groups:

`DomainAdmins`, `EnterpriseAdmins`, `SchemaAdmins`, `Administrators`, `AccountOperators`, `ServerOperators`, `BackupOperators`.

### Entra ID governance KPIs

Entra ID group and user governance counts surfaced through Active Roles and consumed by the `ENT-*` rules:

| KPI Key | Meaning | Membership-dependent |
|---------|---------|:--------------------:|
| `EntraNoGroupOwner` | Entra groups with no owner assigned | ✔ |
| `EntraGuestContainingGroups` | Entra groups that contain guest (external) members | ✔ |
| `EntraSingleOwnerGroups` | Entra groups with only a single owner | ✔ |
| `EntraGuestUsers` | Entra guest (external) user accounts | — |
| `EntraNoManagerUser` | Entra user accounts with no manager set | — |

> Membership-dependent KPIs (see `KpiInfo.EntraMembershipDependentKeys`) require lazily loaded Entra group membership to finish before they are accurate; assessments that use them are gated accordingly (see the Entra Identity Governance note above).

### Synthetic / derived KPIs

Computed from enriched telemetry rather than a single object search:

| KPI Key | Meaning | Source |
|---------|---------|--------|
| `UnsupportedServerOs` | Count of servers on end-of-life server OS | Derived from computer `operatingSystem` |
| `UnsupportedClientOs` | Count of clients on end-of-life client OS | Derived from computer `operatingSystem` |
| `UnsupportedOs` | Combined unsupported OS count | Derived |
| `StaleComputers` | Enabled, non-DC computers inactive beyond threshold | Derived from `lastLogonTimestamp` |
| `KrbtgtPasswordAgeDays` | Age (days) of the krbtgt account password | Targeted `(sAMAccountName=krbtgt)` read of `pwdLastSet` |
| `WeakPasswordLength` | `1` if domain `minPwdLength` < 12, else `0` | Domain root `minPwdLength` |
| `PasswordComplexityDisabled` | `1` if complexity disabled, else `0` | Domain root `pwdProperties` |
| `NoAccountLockout` | `1` if lockout threshold is 0, else `0` | Domain root `lockoutThreshold` |
| `PasswordMaxAgeDays` | Domain maximum password age in days | Domain root `maxPwdAge` |
| `DisabledWorkflows` | Count of disabled Active Roles workflows | Active Roles config |
| `BroadDelegationLinks` | Access Template Links granted to broad principals | Active Roles config (`edsACE` under `CN=AT Links`) |
| `ConfigDatabases` | Number of Active Roles configuration databases | Active Roles config |
| `HistoryDatabases` | Number of Active Roles Management History databases | Active Roles config |

> A KPI that errors or cannot be collected causes its rule(s) to be reported as **Not Applicable** rather than failing.
