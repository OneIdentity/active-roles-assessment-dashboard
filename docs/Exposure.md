# Active Roles Dashboard — MITRE ATT&CK Exposure Reference

[← Back to README](../README.md)

This document describes the **MITRE ATT&CK Exposure** view

The exposure view is **metadata-driven** and works like the assessment engine, but it is a **visibility model, not a scored compliance grade**. A curated, static catalogue of ATT&CK techniques is mapped to dashboard **KPI keys**; at runtime each mapping is evaluated against the live KPI count to derive a per-technique exposure level.

> Source of truth: [`Models/MitreModels.cs`](../Models/MitreModels.cs) (tactics, technique catalogue, KPI mappings and view models) and [`Services/MitreExposureService.cs`](../Services/MitreExposureService.cs) (exposure computation). KPI definitions and the `GetKpiResult` mapping live in [`Models/DashboardModels.cs`](../Models/DashboardModels.cs). The view is rendered by [`Pages/AttackExposure.cshtml`](../Pages/AttackExposure.cshtml).
>
> See also the companion [`Assessments.md`](Assessments.md) — both features read the same `DashboardSummary` KPIs.

---

## 1. How the exposure model works

### Techniques and mappings

Each technique (`MitreTechnique`) has:

| Field | Meaning |
|-------|---------|
| `Id` | ATT&CK technique id (e.g. `T1558.003`). |
| `Name` | ATT&CK technique name. |
| `Tactic` | The tactic (adversary goal) the technique is grouped under. |
| `Description` | Why the technique matters in an AD/Entra context. |
| `Mitigation` | Recommended remediation shown in the UI. |
| `Mappings` | One or more `TechniqueKpiMapping` entries linking dashboard KPIs to the technique. |

Each mapping (`TechniqueKpiMapping`) has:

| Field | Meaning |
|-------|---------|
| `KpiKey` | The KPI whose count contributes to the technique's exposure. |
| `Rationale` | Short label describing why the KPI indicates exposure to the technique. |
| `MediumThreshold` | Count at or above which this KPI raises the technique to at least **Medium** (default `1`). |
| `HighThreshold` | Count at or above which this KPI pushes the technique to **High** on its own (default `10`). |

### Exposure levels

Levels are `None`, `Low`, `Medium`, `High`. For a single KPI mapping, the level is derived from the live count (`LevelFor` in `MitreExposureService`):

- `count <= 0` → **None**
- `count >= HighThreshold` → **High**
- `count >= MediumThreshold` → **Medium**
- otherwise (a positive count below the medium threshold) → **Low**
- If the KPI errored / could not be collected → **None** (and the contribution is flagged `HasError`).

> Because a High weight is expressed by a *low* `HighThreshold` (often `1`), a small count can still elevate exposure — e.g. any single computer with unconstrained delegation is High.

### Aggregation

- A **technique's** exposure is the **maximum** exposure of any KPI mapped to it.
- A **tactic's** exposure (`MaxLevel`) is the maximum exposure of its techniques.
- Within each tactic, techniques are ordered by exposure (highest first), then by id for stable display.
- The overall view (`AttackExposureView`) exposes summary counts: `HighCount`, `MediumCount`, `LowCount`, `CoveredTechniques` (level > None) and `TotalTechniques`.

Exposure is computed from the **current** dashboard summary each time the page is built; it is not saved or compared like assessments.

---

## 2. Tactics

Tactics are displayed in the ATT&CK Enterprise kill-chain order below. Only tactics that have at least one mapped technique are shown.

| Tactic | ATT&CK Id | Display Name |
|--------|-----------|--------------|
| `InitialAccess` | TA0001 | Initial Access |
| `Execution` | TA0002 | Execution |
| `Persistence` | TA0003 | Persistence |
| `PrivilegeEscalation` | TA0004 | Privilege Escalation |
| `DefenseEvasion` | TA0005 | Defense Evasion |
| `CredentialAccess` | TA0006 | Credential Access |
| `Discovery` | TA0007 | Discovery |
| `LateralMovement` | TA0008 | Lateral Movement |
| `Impact` | TA0040 | Impact |

> `Execution` and `Impact` are defined for ordering/completeness but currently have **no mapped techniques**, so they do not appear in the rendered view.

---

## 3. Techniques and KPI mappings

Thresholds are shown as `medium / high` (the count at/above which the KPI reaches Medium / High). Each row is one KPI mapping; a technique with multiple KPIs takes the highest resulting level.

### Initial Access

**`T1078.004` — Valid Accounts: Cloud Accounts**
Guest (external) identities and the groups that contain them provide an attacker-controllable entry vector into the tenant.

| KPI Key | Rationale | Medium/High |
|---------|-----------|-------------|
| `EntraGuestUsers` | Guest (external) accounts are an external foothold into the tenant. | 10 / 50 |
| `EntraGuestContainingGroups` | Groups containing guests extend group-based access to external identities. | 1 / 10 |

### Credential Access

**`T1558.003` — Kerberoasting**
Service tickets for SPN-bearing accounts can be cracked offline.

| KPI Key | Rationale | Medium/High |
|---------|-----------|-------------|
| `SpnUserAccounts` | User accounts with an SPN are directly Kerberoastable. | 1 / 10 |

**`T1558.004` — AS-REP Roasting**
Accounts without Kerberos pre-authentication allow offline cracking.

| KPI Key | Rationale | Medium/High |
|---------|-----------|-------------|
| `NoKerberosPreauth` | Accounts without pre-auth are AS-REP roastable. | 1 / 5 |

**`T1555` — Credentials from Password Stores**
Reversible and DES encryption store/transmit passwords in a recoverable/weak form.

| KPI Key | Rationale | Medium/High |
|---------|-----------|-------------|
| `ReversibleEncryption` | Reversible encryption stores recoverable passwords. | 1 / 1 |
| `UseDesEncryption` | DES is cryptographically weak. | 1 / 5 |

**`T1110` — Brute Force**
Accounts with no password requirement, or non-expiring passwords, weaken resistance to guessing/reuse.

| KPI Key | Rationale | Medium/High |
|---------|-----------|-------------|
| `PasswordNotRequired` | Accounts with no password requirement. | 1 / 1 |
| `PasswordNeverExpires` | Stale, non-rotating credentials. | 10 / 50 |

### Privilege Escalation

**`T1078.002` — Valid Accounts: Domain Accounts**
Highly privileged group membership expands the blast radius of any compromised credential.

| KPI Key | Rationale | Medium/High |
|---------|-----------|-------------|
| `DomainAdmins` | Domain Admins hold full domain control. | 5 / 15 |
| `EnterpriseAdmins` | Enterprise Admins hold forest-wide control. | 2 / 5 |
| `AdminCount` | Accounts flagged as protected/privileged. | 20 / 75 |

**`T1484.001` — Domain Policy Modification: Group Policy Modification**
Membership in powerful legacy operator groups grants directory- or DC-level capabilities.

| KPI Key | Rationale | Medium/High |
|---------|-----------|-------------|
| `AccountOperators` | Account Operators can manage most users and groups. | 1 / 3 |
| `ServerOperators` | Server Operators can sign in to and control domain controllers. | 1 / 3 |
| `BackupOperators` | Backup Operators can read/restore any file (escalation path). | 2 / 5 |

### Defense Evasion

**`T1078` — Valid Accounts (Expired / Locked)**
Expired or locked accounts left enabled create inconsistent state that can mask malicious re-use.

| KPI Key | Rationale | Medium/High |
|---------|-----------|-------------|
| `ExpiredUsers` | Expired accounts still present. | 5 / 25 |
| `UserAccountLockedOut` | Locked accounts may indicate active attacks. | 3 / 15 |

### Discovery

**`T1069` — Permission Groups Discovery**
Empty or unowned groups complicate the review of enumerable effective access.

| KPI Key | Rationale | Medium/High |
|---------|-----------|-------------|
| `NoGroupOwner` | Unowned groups lack review. | 5 / 25 |
| `EntraNoGroupOwner` | Unowned Entra groups add unreviewed, enumerable access scope. | 5 / 25 |
| `EmptyGroups` | Empty groups add enumeration noise. | 10 / 50 |

### Lateral Movement

**`T1550` — Use Alternate Authentication Material (Delegation Abuse)**
Unconstrained delegation lets a compromised host impersonate any user that authenticates to it.

| KPI Key | Rationale | Medium/High |
|---------|-----------|-------------|
| `UnconstrainedComputers` | Computers trusted for unconstrained delegation are high-value pivot points. | 1 / 1 |
| `TrustedForDelegation` | User accounts trusted for unconstrained delegation. | 1 / 5 |

**`T1550.003` — Use Alternate Authentication Material: Pass the Ticket**
SPN service accounts and delegation-trusted accounts broaden ticket forging/reuse (incl. Silver Tickets).

| KPI Key | Rationale | Medium/High |
|---------|-----------|-------------|
| `SpnUserAccounts` | SPN user accounts enable Silver Ticket / ticket reuse. | 1 / 10 |
| `TrustedForDelegation` | Delegation-trusted accounts broaden ticket reuse. | 1 / 5 |

### Persistence

**`T1098` — Account Manipulation**
Stale, orphaned, or unmanaged accounts provide durable footholds that evade routine review.

| KPI Key | Rationale | Medium/High |
|---------|-----------|-------------|
| `NeverLoggedIn` | Dormant accounts are prime persistence candidates. | 5 / 25 |
| `StaleUsers` | Enabled but inactive accounts are durable, low-visibility footholds. | 5 / 25 |
| `NoManagerServiceAccount` | Unowned service accounts evade review. | 1 / 10 |
| `DeprovisionedUsers` | Deprovisioned-but-present accounts. | 1 / 10 |

**`T1136` — Create Account**
Deprovisioned/long-inactive accounts can be silently reactivated to establish persistent access.

| KPI Key | Rationale | Medium/High |
|---------|-----------|-------------|
| `DeprovisionedUsers` | Deprovisioned accounts that persist can be reactivated. | 10 / 50 |
| `StaleUsers` | Inactive enabled accounts are candidates for takeover/reuse. | 5 / 25 |

---

## 4. KPI coverage

The exposure model reads each mapped KPI count via `DashboardSummary.GetKpiResult(kpiKey)` — the same accessor used by the assessment engine (see [`Assessments.md`](Assessments.md) for full KPI definitions and sources).

### KPIs referenced by exposure techniques

`EntraGuestUsers`, `EntraGuestContainingGroups`, `EntraNoGroupOwner`, `SpnUserAccounts`, `NoKerberosPreauth`, `ReversibleEncryption`, `UseDesEncryption`, `PasswordNotRequired`, `PasswordNeverExpires`, `DomainAdmins`, `EnterpriseAdmins`, `AdminCount`, `AccountOperators`, `ServerOperators`, `BackupOperators`, `ExpiredUsers`, `UserAccountLockedOut`, `NoGroupOwner`, `EmptyGroups`, `UnconstrainedComputers`, `TrustedForDelegation`, `NeverLoggedIn`, `StaleUsers`, `NoManagerServiceAccount`, `DeprovisionedUsers`.

### Notes

- `EntraNoGroupOwner` is **membership-dependent**: it requires lazily loaded Entra group membership to be populated before it is accurate. The Exposure page reuses the cached dashboard summary so it reflects membership state consistently with the rest of the dashboard.
- A KPI that errors or cannot be collected contributes **None** exposure (flagged as an error contribution) rather than inflating the level.

---

## 5. Relationship to Assessments

| | Assessments | Exposure |
|--|-------------|----------|
| Purpose | Scored security/compliance posture | ATT&CK visibility / attack-surface view |
| Catalogue | Static rule library (`AssessmentRuleLibrary`) | Static technique library (`MitreTechniqueLibrary`) |
| Unit | Rule → KPI, warn/fail thresholds | Technique → KPI(s), medium/high thresholds |
| Output | Pass / Warn / Fail, weighted **Score & Grade** | Per-technique **exposure level** (None/Low/Medium/High) |
| Persistence | Saved runs, history, **compare** | Computed live; history **derived from snapshots** (never saved separately) |
| Data source | `DashboardSummary` KPIs | The same `DashboardSummary` KPIs |

---

## 6. Compare & trend (derived from snapshots)

Exposure is a deterministic projection of KPI counts, so it is never persisted on its
own. Instead, the Exposure page reuses the [Snapshots](Snapshots.md) store to provide
historical comparison and trend analysis — this avoids a second persistence layer and
keeps history immune to threshold drift (levels are always recomputed with the *current*
`MitreTechniqueLibrary`).

### How it is built

- `MitreExposureService.Build(Func<string,(int Count,string? Error)>)` is the KPI-lookup
  core; `Build(DashboardSummary)` (live) and `BuildFromSnapshot(Snapshot)` (historical)
  both delegate to it, so a saved snapshot yields the same exposure view it would have
  produced live at capture time.
- `Compare(from, to, fromLabel, toLabel, toIsCurrent)` diffs two exposure views by
  **technique id**, classifying each row as `Increased`, `Decreased`, `Unchanged`,
  `Added`, or `Removed`. A **rising** exposure level is `Bad` (worse posture) and a
  **falling** level is `Good`.
- `BuildTrend(snapshotsOldestFirst)` recomputes exposure for every saved snapshot via
  `BuildFromSnapshot`, producing a per-technique numeric level series (`0`=None … `3`=High,
  `null` where a technique's KPIs were absent) plus aggregate High/Medium/Low counts per
  timestamp.

### On the page

`AttackExposureModel` injects `SnapshotService`, lists saved snapshots as baselines, and
binds `FromId` / `ToId` (query, `SupportsGet`). A reserved `ToId=current` compares a
baseline snapshot against the **live** exposure view. The view renders a baseline/compare
selector, a per-tactic comparison table with directional arrows, and a High/Medium/Low
trend chart.
