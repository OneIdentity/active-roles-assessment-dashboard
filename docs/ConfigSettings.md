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
