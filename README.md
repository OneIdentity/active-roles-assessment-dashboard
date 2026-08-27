# Active Roles Dashboard

The dashboard is an ASP.NET Core (.NET 9) Razor Pages web application that connects to a One Identity
Active Roles installation through its REST API, surfaces key environment health and
governance metrics as an interactive dashboard, and lets you export any view to
PDF, Microsoft Word, or Microsoft Excel.

> **Important — no support.** This project is published as an open-source utility.
> One Identity LLC does not provide support, updates, or guarantees of any kind.
> See `LICENSE.txt` for full terms.

## What the dashboard contains

The dashboard is split into five dashboards: A main dashboard and four category dashboards
- **Active Roles** - Metrics on key Active Roles configuration objects
- **Active Directory** - Metrics on key Active Directory objects managed by Active Roles
- **Entra ID** - Metrics on key Entra ID objects managed by Active Roles
- **Licensing** - License usage data

The main dashboard shows a summary of key data across all categories, while each category dashboard
presents KPI cards, native charts, and searchable/sortable drill-down tables.

For the full breakdown of every category and KPI under each dashboard, see
**[docs/Dashboards.md](docs/Dashboards.md)**.

Each KPI supports a drill-down panel backed by configurable LDAP filters and
attributes. The entire dashboard, a single category, or a single KPI can be
exported.

### Additional tools

Beyond the live dashboards, it adds governance tooling.

| Tool | Purpose | Reference |
| --- | --- | --- |
| **Snapshots** | Capture point-in-time KPI counts to saved JSON files, then view trends and compare two points in time (or a saved baseline against live data). | [docs/Snapshots.md](docs/Snapshots.md) |
| **Assessments** | Run scored security/compliance assessments against KPI thresholds, producing a score and grade; save runs and compare them. | [docs/Assessments.md](docs/Assessments.md) |
| **MITRE ATT&CK Exposure** | A visibility view mapping curated ATT&CK techniques to KPIs to highlight attack-surface exposure. | [docs/Exposure.md](docs/Exposure.md) |
| **Group Tree** | Explore nested (direct and indirect) group membership as an expandable tree. | — |

These features are all computed from the same live dashboard KPIs.

> **Note.** Some tools are not available when Entra ID group membership is being retrieved. This is to avoid incorrect results.
> See [Data loading and refresh](#data-loading-and-refresh) for more information.

### Export formats

| Format | Engine | Notes |
| --- | --- | --- |
| PDF | QuestPDF | Branded layout with KPI tiles, charts, and detail tables. |
| Word (`.docx`) | Open XML SDK | Mirrors the PDF appearance. |
| Excel (`.xlsx`) | Open XML SDK | A **Summary** sheet plus one tab per KPI (`<category> - <kpi>`), with native Excel Tables for filtering and Summary hyperlinks that jump to each detail tab. |

## Requirements

| Requirement | Notes |
| --- | --- |
| .NET 9 SDK / ASP.NET Core Runtime | Required to build and run the application. |
| One Identity Active Roles | A reachable Active Roles installation exposing the REST API and RSTS token endpoint. |
| Active Roles REST API + RSTS | The dashboard authenticates via RSTS (OAuth2) and calls the Active Roles Web API. |
| Network access | Outbound HTTPS from the host running the app to the Active Roles RSTS and API endpoints. |
| Browser | A modern browser to view the dashboard. Chart.js is bundled locally (no CDN required). |

The dashboard has been built and tested against Active Roles 8.6.

## Quick start

1. Clone the repository and open it in Visual Studio 2026 (or use the .NET CLI).
2. Configure the Active Roles connection in `appsettings.json` (see **Configuration**).
3. Restore and run:

   ```powershell
   dotnet restore
   dotnet run
   ```

4. Browse to the URL shown in the console (for example `https://localhost:5001`).
5. On first run, if `ApiBaseUrl` is not configured you are redirected to the
   **Setup** wizard to enter the Active Roles connection details.
6. Sign in with an Active Roles-enabled account and open a category to view KPIs.

## Installation Considerations

The dashboard can be installed on any IIS server that can communicate with the Secure Token Service (RSTS)
and the Active Roles REST API. For convenience, it is recommended to install it on a server running the Active
Roles Web Interface.
- The Dashboard should be set up as an application within an IIS web site (the same IIS web site as the Web Interface is recommended
- The app pool for the IIS web site must have the .NET CLR Version set to 'No Managed Code'
- For the 'Open in Web Interface' feature to work, you must change the following settings in the Active Roles Web Interface sites web.config file
  - appSettings -> EnableRequestValidation false
  - appSettings -> EnableAntiForgery false

## Configuration

Connection and default query settings live under the `ActiveRoles` section of
`appsettings.json`. The essentials to get started are:

| Setting | Description |
| --- | --- |
| `ActiveRoles:ApiBaseUrl` | Base URL of the Active Roles REST API (for example `https://server:5000/api/v1`). |
| `ActiveRoles:RstsUrl` | RSTS OAuth2 token endpoint used for authentication. |
| `ActiveRoles:WebInterfaceUrl` | Active Roles Web Interface URL (used for deep links). |
| `ActiveRoles:Resource` | OAuth2 resource identifier (default `ActiveRoles`). |
| `ActiveRoles:IgnoreSslErrors` | Set `true` only for lab/self-signed environments to bypass TLS validation. |

For the complete list of settings — base DNs, KPI filters, storage folders, and
thresholds/tuning — see **[docs/ConfigSettings.md](docs/ConfigSettings.md)**.

> **Tip.** For production, avoid setting `IgnoreSslErrors` to `true`; install a
> trusted certificate on the Active Roles endpoints instead.

## Usage

1. Select a dashboard (Active Directory, Active Roles, Entra ID, Licensing).
2. Select a category (use the Expand/Collapse button to view or hide the categories KPIs)
3. Click a KPI card to open its drill-down panel with a searchable, sortable table.
4. Use the toolbar **Export** button to choose scope (Dashboard / Category / KPI),
   format (PDF / Word / Excel), and whether to include detail tables.
5. The generated file downloads through the browser.

## Navigation

When navigating between the dashboard, use the dashboard tiles, the Home button and the 'Back to Dashboard' links.
Do not use the browser back/forward buttons.

## Data loading and refresh
The dashboard has been designed to work with live data rather than static, stored data, but performance in large
environments has been taken into consideration. The data load/refresh works as follows:
- The main data set is loaded once a user successfully logs into the dashboard. The majority of the KPIs are derived from this.
- The category dashboards load their KPI data from the main dataset when they are selected
- The membership of Entra Id groups is loaded in the background when the Entra Id dashboard is selected. This is because the membership of performance reasons. The dashboard provides visual indicators on the data loading progress.
- Clicking the Refresh button on a category dashboard reloads the KPI data from the main dataset
- Clicking the Refresh button the main dashboard reloads the main dataset from Active Directory and Entra ID

## Output files

| Output | Location |
| --- | --- |
| PDF report | Downloaded by the browser (`.pdf`). |
| Word report | Downloaded by the browser (`.docx`). |
| Excel report | Downloaded by the browser (`.xlsx`) — Summary tab plus one tab per KPI. |
| Application log | Standard ASP.NET Core logging (console / configured providers). |

## Troubleshooting

| Symptom | Likely cause | Resolution |
| --- | --- | --- |
| Redirected to the Setup wizard on startup | `ActiveRoles:ApiBaseUrl` is not configured | Complete the Setup wizard or set the value in `appsettings.json`. |
| Login fails / token errors | Wrong `RstsUrl`/`Resource`, or the account lacks Active Roles access | Verify the RSTS endpoint and credentials against the Active Roles Web API. |
| TLS/certificate errors connecting to Active Roles | Self-signed or untrusted certificate on the AR endpoints | Install a trusted certificate, or set `IgnoreSslErrors` to `true` for non-production only. |
| KPI tables are empty | LDAP filter or base DN does not match the environment | Adjust the corresponding `Default*Filter` / `Custom*` settings. |
| Charts do not render | JavaScript blocked in the browser | Chart.js is bundled locally; ensure scripts are allowed for the site. |
| Excel export has only a Summary sheet | "Include details" was unchecked in the export dialog | Re-export with detail tables enabled. |
| Slow drill-downs on large domains | Broad LDAP filters returning many objects | Narrow the filters/base DNs for the affected KPIs. |

## Documentation

Feature reference documents live under [`docs/`](docs/):

- [Dashboards.md](docs/Dashboards.md) — full breakdown of dashboards, categories, and KPIs.
- [Snapshots.md](docs/Snapshots.md) — snapshot capture, storage, comparison, and trends.
- [Assessments.md](docs/Assessments.md) — assessment types, rules, scoring, and compare.
- [Exposure.md](docs/Exposure.md) — MITRE ATT&CK technique-to-KPI mappings and exposure scoring.
- [ConfigSettings.md](docs/ConfigSettings.md) — full reference for every `appsettings.json` configuration setting.
- [Deployment.md](docs/Deployment.md) — deploying to a target IIS server, the deploy script, and protecting the service-account secret.
- [Localization.md](docs/Localization.md) — localization architecture, resource conventions, and how to add languages or translations.
- [KnownIssues.md](docs/KnownIssues.md) — known issues, limitations, and their workarounds.

## Technology

- ASP.NET Core Razor Pages (.NET 9)
- Cookie authentication with RSTS (OAuth2) token acquisition against Active Roles
- QuestPDF (PDF), DocumentFormat.OpenXml (Word and Excel)
- Bootstrap and Chart.js (bundled locally)

## License

Copyright 2026 One Identity LLC. Licensed under the One Identity Permissive
Software License. See `LICENSE.txt` for full terms.

## About

A .NET 9 Razor Pages dashboard that connects to a One Identity Active Roles
installation, presents environment KPIs across Active Directory, Active Roles,
Entra ID, and Licensing categories, and exports any view to PDF, Word, or Excel.
It also provides snapshots (with trend and comparison), scored assessments, a
MITRE ATT&CK exposure view, and a nested group membership tree.
