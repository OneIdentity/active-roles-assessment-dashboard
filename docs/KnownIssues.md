# Known Issues

This document tracks known issues, limitations, and their workarounds for the
Active Roles Dashboard.

> **Important — no support.** This project is published as an open-source utility.
> One Identity LLC does not provide support, updates, or guarantees of any kind.
> See `LICENSE.txt` for full terms.

## How to use this document

Each entry describes the observed symptom, the underlying cause, and a workaround
(if one exists). Issues are grouped by area. If you hit a problem that is not
listed here, first check the **Troubleshooting** table in the
[README](../README.md#troubleshooting).

## Setup and configuration

| Issue | Details | Workaround |
| --- | --- | --- |
| Cache does not build until setup completes | On first run, while `ActiveRoles:ApiBaseUrl` is empty the background superset loader intentionally skips collection and logs a "not configured" message. | Complete the Setup wizard. Finishing setup triggers an immediate cache build; no restart is required. |
| Setup wizard writes to the environment-specific settings file | When an `appsettings.<Environment>.json` file already exists, the wizard writes the connection details and service-account credentials to that file instead of `appsettings.json`. | Expected behaviour. Confirm the correct file was updated for your environment (for example `appsettings.Development.json` under the Development environment). |
| Protected service-account password fails to decrypt after a move | The password is encrypted with ASP.NET Core Data Protection, which is tied to the machine/content-root key ring. Copying `appsettings.*.json` to a different host makes the protected value undecryptable. | Re-run the Setup wizard (or the `--protect-secret` helper) on the target host so the password is protected with that host's key ring. |
| Rsts with multiple providers not supported | The service does not support multiple RSTS providers. | Use a single RSTS provider. |

## Connectivity

| Issue | Details | Workaround |
| --- | --- | --- |
| "Data cache could not be built" on the login page | The Active Roles REST API was unreachable during cache build (service stopped, wrong `ApiBaseUrl`, DNS/TLS failure, or connection refused). | Verify the Active Roles Administration Service is running and that `ApiBaseUrl`/`RstsUrl` are correct and reachable from the host. |
| TLS/certificate errors connecting to Active Roles | The Active Roles endpoints present a self-signed or untrusted certificate. | Install a trusted certificate on the AR endpoints. As a non-production-only measure, `ActiveRoles:IgnoreSslErrors` can be set to `true`. |

## Data loading and refresh

| Issue | Details | Workaround |
| --- | --- | --- |
| Some tools are unavailable while Entra ID membership loads | Snapshots, assessments, and other derived tools are temporarily disabled while Entra ID group membership is being retrieved, to avoid producing incorrect results. | Wait for membership loading to complete; the tools re-enable automatically. |
| Slow drill-downs on large domains | Broad LDAP filters or base DNs return large result sets. | Narrow the `Default*Filter` / `Custom*` settings for the affected KPIs. |
| KPI limitations | The dashboard is dependent on the Active Roles REST API, which has some limitations, so some KPIs are not currently available. | Future releases of Active Roles will address these limitations. |

## Localization

| Issue | Details | Workaround |
| --- | --- | --- |
| A string appears in English under a non-English culture | The corresponding key is missing from that culture's `.resx`, so the English fallback is used. | Add the missing key/value to the appropriate `Resources/**/*.<culture>.resx` file. See [Localization.md](Localization.md). |

## Reporting a new issue

Because this project is unsupported, there is no formal issue queue. If you extend
or fork the dashboard, record newly discovered issues here so downstream users are
aware of them.
