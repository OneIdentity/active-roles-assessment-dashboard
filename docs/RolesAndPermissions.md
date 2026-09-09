# Active Roles Dashboard — Roles & Permissions Reference

[← Back to README](../README.md)

This document describes the **role-based access control (RBAC)** model: the fixed set of **roles**, the fixed set of **permissions**, the **default role → permission matrix**, how a user's role is **resolved**, and where each permission is **enforced**.

> Source of truth: [`Models/RoleModel.cs`](../Models/RoleModel.cs) (the `DashboardRole` and `DashboardPermission` enums plus `RolePermissionRegistry`) and [`Services/RoleService.cs`](../Services/RoleService.cs) (role evaluation and the encrypted, editable matrix). The shared enforcement helpers live in [`Pages/DashboardPageModel.cs`](../Pages/DashboardPageModel.cs). The role matrix is edited on the **System settings** category of [`Pages/Settings.cshtml`](../Pages/Settings.cshtml).

---

## 1. Roles

Roles are a **closed enum set** — roles cannot be added or removed, only their permission assignments are editable. Roles are listed highest privilege first.

| Role | Description |
|------|-------------|
| **Dashboard Administrator** | Full access to everything, including System settings and the role matrix. This row is **fixed** and not editable. Active Roles administrators are always Dashboard Administrators. |
| **Auditor** | Sees and can do everything **except** managing System settings. Full read visibility across the environment (not scoped by delegation). |
| **Power User** | Sees what their delegated Active Roles permissions allow and can perform some functions. |
| **User** | The base role, assigned when a user matches no other role group. Sees what their delegated permissions allow and performs limited functions. |

### How a role is resolved

1. **Active Roles administrators** are always resolved as **Dashboard Administrator**, independently of any role group.
2. Otherwise the user is evaluated against the configured **role groups** in `EvaluationOrder` (Dashboard Administrator → Auditor → Power User). The **first** role whose group the user is a member of wins.
3. If the user matches no role group, they are assigned the base **User** role.

Group membership is resolved via the **service-account token** (end-user tokens cannot read this) and cached in session alongside the resolved role and admin flag.

---

## 2. Delegated (per-user) visibility vs. full visibility

A key concept is the **`UseDelegatedPermissionsForVisibility`** permission:

- Roles **without** it (Dashboard Administrator, Auditor) see the **full, unfiltered superset** of dashboard data.
- Roles **with** it (Power User, User) see a **per-user projection** filtered to the data their own Active Roles delegation allows (their SID + nested group SIDs).

For the **Active Directory**, **Entra ID**, **Exchange**, and **Licensing** dashboards, a user may therefore gain visibility **either** through an explicit *View …dashboard* permission **or** through delegated/deployment visibility to some of that dashboard's data. This is why the visibility helpers fold both signals together.

---

## 3. Permissions

Permissions are a **closed enum set**. Each one gates a specific view or action and is enforced **server-side** (not just hidden in the UI).

| Permission | Governs | Enforced at |
|------------|---------|-------------|
| **Manage User settings** | View and change the *User settings* category (language, KPI visibility, auto-refresh). | `Settings` page (view + POST). |
| **View System settings** | View the *System settings* category (read-only). | `Settings` page. |
| **Manage System settings** | Modify the *System settings* category (REST API, filters, licensing, thresholds). | `Settings` page (POST). |
| **View Active Roles dashboard** | See the Active Roles configuration dashboard. | Tile visibility + direct-URL guard. |
| **View Active Directory dashboard** | See the AD dashboard (or delegated AD data). | Tile visibility + direct-URL guard. |
| **View Entra Id dashboard** | See the Entra ID dashboard (or delegated Entra data). | Tile visibility + direct-URL guard. |
| **View Exchange dashboard** | See the Exchange dashboard (requires Exchange deployed + view/admin/delegated). | Tile visibility + direct-URL guard. |
| **View Licensing dashboard** | See the Licensing dashboard (or delegated licensing data). | Tile visibility + direct-URL guard. |
| **Use delegated permissions to determine dashboard visibility** | Scope dashboard data to the user's own Active Roles delegation instead of the full superset. | Summary projection (per-user SID filter). |
| **View snapshots** | See the Snapshots button and page. | Toolbar + direct-URL guard. |
| **Compare snapshots** | Compare two snapshots (or a baseline vs. live). | Compare controls + comparison handler. |
| **Run & Save snapshots** | Capture and persist new snapshots. | Capture control + `OnPostCapture`. |
| **Delete snapshots** | Delete saved snapshots. | Delete control + `OnPostDelete`. |
| **View Exposure report** | See the MITRE ATT&CK Exposure button and page. | Toolbar + direct-URL guard. |
| **Compare Exposure reports** | Compare exposure reports. | Compare controls + comparison handler. |
| **View Assessments** | See the Assessments button and page. | Toolbar + direct-URL guard. |
| **Run & Save assessments** | Run and persist new assessments. | Run control + `OnPostRun`. |
| **Compare assessments** | Compare assessments. | Compare controls + comparison handler. |
| **Delete assessments** | Delete saved assessments. | Delete control + `OnPostDelete`. |
| **Export assessments** | Export a saved assessment as a document. | Export control + `OnPostExport`. |
| **Rebuild cache** | Trigger a manual rebuild of the shared service-account cache. | Rebuild button + `OnPostRefresh`. Active Roles admins always may. |
| **Export Dashboard Data** | Export dashboard/category/KPI data. Export options are further filtered to the dashboards the user may view. | Export button + `/Export` controller (403 on violation). |

> **Refreshing a dashboard** (the header Refresh button) is a plain page reload and requires **no** dedicated permission — anyone who can view a dashboard may refresh it. This is distinct from **Rebuild cache**, which re-queries the whole environment.

---

## 4. Default role → permission matrix

This is the **default** assignment used when no persisted matrix exists. Administrators may edit every row **except Dashboard Administrator** via the System settings category. The persisted matrix is stored **encrypted**.

Legend: ● = granted by default, — = not granted.

| Permission | Dashboard Administrator | Auditor | Power User | User |
|------------|:----------------------:|:-------:|:----------:|:----:|
| Manage User settings | ● | ● | ● | ● |
| View System settings | ● | ● | ● | — |
| Manage System settings | ● | — | — | — |
| View Active Roles dashboard | ● | ● | — | — |
| View Active Directory dashboard | ● | ● | — | — |
| View Entra Id dashboard | ● | ● | — | — |
| View Exchange dashboard | ● | ● | — | — |
| View Licensing dashboard | ● | ● | — | — |
| Use delegated permissions for visibility | — | — | ● | ● |
| View snapshots | ● | ● | — | — |
| Compare snapshots | ● | ● | — | — |
| Run & Save snapshots | ● | ● | — | — |
| Delete snapshots | ● | ● | — | — |
| View Exposure report | ● | ● | — | — |
| Compare Exposure reports | ● | ● | — | — |
| View Assessments | ● | ● | — | — |
| Run & Save assessments | ● | ● | — | — |
| Compare assessments | ● | ● | — | — |
| Delete assessments | ● | ● | — | — |
| Export assessments | ● | ● | — | — |
| Rebuild cache | ● | ● | — | — |
| Export Dashboard Data | ● | ● | ● | — |

Notes:

- **Dashboard Administrator** receives **every** permission **except** *Use delegated permissions for visibility* — administrators see the full, unfiltered environment, so delegated scoping does not apply to them.
- **Auditor** has full read/act visibility across the environment but **cannot manage System settings** and does not use delegated scoping.
- **Power User** and **User** rely on **delegated visibility** — they see dashboards only where they have some in-scope data. Assessments (as sensitive, environment-wide reports) are deliberately **not** granted to them; dashboard data export **is** granted to Power Users but constrained to the dashboards they can view.
- **Delete snapshots / assessments** are granted to Auditor by default; administrators may revoke them if a stricter, admin-only deletion policy is preferred.

---

## 5. Enforcement model

Permissions are enforced with **defense in depth**:

1. **UI gating** — buttons/links are hidden or disabled when the permission is absent (e.g. the Export, Snapshots, Assessments, and Rebuild Cache toolbar icons).
2. **Direct-URL protection** — page `OnGet` handlers redirect or `Forbid()` when a user navigates directly to a page they lack permission for.
3. **Action gating** — POST handlers (capture, run, delete, export, rebuild) re-check the permission, so a crafted request cannot bypass the hidden UI.
4. **Content scoping** — where a feature aggregates multiple dashboards (dashboard export), the content is filtered to the dashboards the user is permitted to view, and the server re-validates the requested scope (`403` on violation).

The Settings POST handler is the canonical example: it rejects entirely without any settings permission, saves *User settings* only with **Manage User settings**, saves *System settings* only with **Manage System settings**, and persists the **role matrix** only for **Active Roles administrators**.

---

## 6. Editing the matrix

The per-role permission assignments (every row except Dashboard Administrator) are edited on the **System settings** category of the Settings page and persisted **encrypted** via `RoleService`. Changing a role's permissions takes effect on the affected users' next role resolution.
