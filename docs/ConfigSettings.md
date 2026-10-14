# Active Roles Dashboard — Configuration Settings Reference

[← Back to README](../README.md)

Connection and default query settings live under the `ActiveRoles` section of
`appsettings.json`. This document lists every configurable setting and its default.

## Connection

| Setting | Description |
| --- | --- |
| `ActiveRoles:ApiBaseUrl` | Base URL of the Active Roles REST API (for example `https://server:5000/api/v1`). |
| `ActiveRoles:RstsUrl` | RSTS OAuth2 token endpoint used for authentication. |
| `ActiveRoles:WebInterfaceUrl` | Active Roles Web Interface URL (used for deep links). |
| `ActiveRoles:Resource` | OAuth2 resource identifier (default `ActiveRoles`). |
| `ActiveRoles:IgnoreSslErrors` | Set `true` only for lab/self-signed environments to bypass TLS validation. |

> **Tip.** For production, avoid setting `IgnoreSslErrors` to `true`; install a
> trusted certificate on the Active Roles endpoints instead.

## Base DNs

| Setting | Description |
| --- | --- |
| `ActiveRoles:DefaultActiveDirectoryDN` | Virtual container spanning managed domains (default `CN=Active Directory`). |
| `ActiveRoles:DefaultARConfigurationDN` | Active Roles configuration container (default `CN=Configuration`). |
| `ActiveRoles:DefaultAzureConfigurationDN` | Base DN under which connected Azure/Entra tenants are exposed (default `CN=Azure,CN=Configuration`). |

## KPI queries

| Setting | Description |
| --- | --- |
| `ActiveRoles:Default*Filter` | Built-in LDAP filters powering each KPI drill-down. |
| `ActiveRoles:Custom*` | Optional overrides for base DNs, filters, and attributes per KPI. |

## Storage

| Setting | Description |
| --- | --- |
| `ActiveRoles:SnapshotDirectory` | Folder where snapshot JSON files are stored (default `App_Data/Snapshots`; relative paths resolve under the content root). |
| `ActiveRoles:AssessmentDirectory` | Folder where saved assessment results are stored (default `App_Data/Assessments`; relative paths resolve under the content root). |

## Thresholds and tuning

| Setting | Description |
| --- | --- |
| `ActiveRoles:MaxGroupTreeDepth` | Maximum depth when expanding nested group membership in the Group Tree (default `10`). |
| `ActiveRoles:StaleAccountThresholdDays` | Days without an interactive logon after which an enabled account is considered stale (default `90`). |
| `ActiveRoles:EntraLargeGroupMemberThreshold` | Member count at or above which an Entra group is flagged as "large" (default `100`). |
| `ActiveRoles:EntraMembershipFetchConcurrency` | Maximum concurrent per-group Entra membership fetches during lazy loading (default `8`). |
| `ActiveRoles:EntraMembershipBatchSize` | Number of Entra groups requested per membership-loading batch (default `40`). |
| `ActiveRoles:EntraMembershipToastDelayMs` | Delay before the "loading group membership" toast appears, so fast loads don't flash it (default `500`). |

## KPI thresholds and exclusions

Both sections can be edited on the **Settings** page (cards *KPI Thresholds* and *KPI Exclusions*), which persists them to `appsettings.json`.

### `ActiveRoles:KpiThresholds`

Per-KPI severity bands used by the dashboard (tile severity), Assessments and Attack Exposure. Keyed by KPI key. **None** is fixed at `0` and **Low** is any count above 0 and below Medium; only **Medium** and **High** are configurable. KPIs without an entry use the built-in defaults.

| Property | Description |
| --- | --- |
| `Mode` | `Count` (absolute object counts, default) or `Percent` (0-100 of the related total). Applies to both Medium and High. |
| `Medium` / `High` | Threshold values. High must be greater than or equal to Medium. |
| `TotalKpiKey` | Optional KPI key used as the total in `Percent` mode (defaults to users, groups or computers based on the KPI). |

```json
"KpiThresholds": {
  "PasswordNeverExpires": { "Mode": "Percent", "Medium": 5, "High": 15 }
}
```

### `ActiveRoles:KpiExclusions`

Excludes known false positives (for example `krbtgt`) from KPI counts. No exclusions are configured by default.

| Property | Description |
| --- | --- |
| `Global` | Patterns applied to every KPI. |
| `PerKpi` | Patterns per KPI key, applied in addition to `Global`. Patterns that repeat a global pattern are removed when saved from Settings. |

Matching rules:

- One pattern per entry; `*` matches any sequence and `?` a single character. Matching is case-insensitive.
- A pattern matches an object when it matches its **Name** or its **Distinguished Name** (as shown in the KPI drill-down).
- Excluded objects are still listed in the drill-down, marked **Excluded**, but are not counted in the KPI tile, charts, severity, Assessments, Attack Exposure or new snapshots.
- A count can only be adjusted when the KPI's drill-down lists every counted object and has a Name or Distinguished Name column. For other KPIs (for example aggregates, or truncated detail lists) the raw count is kept and the KPI is flagged with `*` on the dashboard and a notice in its drill-down.
- Snapshots store the counts as they were at capture time. Snapshots taken before exclusions were added or changed keep their original counts, so trend and compare views can show a step change that is caused by configuration rather than by the directory.

```json
"KpiExclusions": {
  "Global": [ "krbtgt" ],
  "PerKpi": {
    "PasswordNeverExpires": [ "CN=svc_*,OU=Service Accounts,*" ]
  }
}
```

## Hosting

| Setting | Description |
| --- | --- |
| `PathBase` | Application base path when hosted under a reverse proxy or IIS sub-application. |

## Visibility

Every dashboard category and KPI can be individually enabled or disabled. Visibility
is a **per-user** preference managed from the in-app **Settings** page — not from
`appsettings.json` — and is persisted as a JSON file per user under the `usersettings`
folder in the content root. Disabling a category or KPI hides it from the dashboards
and excludes it from exports; the underlying data is not queried when nothing that
needs it is enabled.
