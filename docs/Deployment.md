# Deployment

This guide covers deploying the Active Roles Dashboard to a target IIS server for
repeatable, multi-user testing or production hosting. It explains the deploy
script, how machine-specific secrets are protected, and the manual steps required
on each new target.

## Overview

The dashboard is a framework-dependent ASP.NET Core (.NET 9) application hosted in
IIS out-of-process (see [`web.config`](../web.config)). Deployment is automated by
[`deploy/Publish-ToServer.ps1`](../deploy/Publish-ToServer.ps1), which:

1. Runs `dotnet publish` (Release, framework-dependent).
2. Stops the target IIS application pool to release locked DLLs.
3. Mirrors the publish output to the target with `robocopy /MIR`, **excluding**
   machine-specific state so redeploys never clobber it:
   - `appsettings.json` &mdash; the target's service-account config and its
	 machine-encrypted `ProtectedPassword`.
   - `App_Data\` &mdash; the Data Protection key ring that decrypts
	 `ProtectedPassword`.
4. Optionally seeds `appsettings.json` on a fresh machine (`-IncludeAppSettings`).
5. Restarts the application pool.

> Note: The IIS Application and Application Pool must already exist on the target. The deploy script does not create them. See pre-requisites below.

## Prerequisites

| Requirement | Notes |
| --- | --- |
| .NET 9 SDK (on the build machine) | Required to run `dotnet publish`. |
| ASP.NET Core 9 Hosting Bundle (on the target) | Required because the app is published framework-dependent. |
| IIS site + application pool (on the target) | The app pool name is passed to the script. |
| Remote PowerShell / WinRM to the target | Used to stop/start the app pool remotely. |
| Administrative share access (`\\<Target>\C$`) | Used by `robocopy` to copy files. |

## The deploy script

```powershell
# First-time deploy, seeding appsettings.json:
.\deploy\Publish-ToServer.ps1 -Target <your-target-machine> -AppPool <your-app-pool> -IncludeAppSettings

# Normal repeat deploy (preserves the target's appsettings.json + Data Protection keys):
.\deploy\Publish-ToServer.ps1 -Target <your-target-machine> -AppPool <your-app-pool>
```

### Parameters

| Parameter | Required | Description |
| --- | --- | --- |
| `-Target` | Yes | Target machine name, e.g. `winemeaapp04`. |
| `-AppPool` | Yes | IIS application pool to stop/start on the target. |
| `-RemotePath` | No | UNC path to the site folder. Defaults to `\\<Target>\C$\inetpub\wwwroot\ActiveRolesDashboard`. |
| `-IncludeAppSettings` | No | Also copy `appsettings.json` (first deploy or when intentionally updating config). |

> **Verify the site path.** If your site is not under `inetpub\wwwroot\ActiveRolesDashboard`,
> pass the correct location with `-RemotePath`. Copying to the wrong folder is a
> common cause of "the files didn't update".

## Service-account secret protection

The startup cache loader authenticates to Active Roles with a dedicated service
account. Its password is **never stored in plaintext**. Instead it is encrypted
with ASP.NET Core Data Protection and stored in `appsettings.json` under
`ActiveRoles:ServiceAccount:ProtectedPassword`.

Data Protection keys are **machine- and key-ring-specific**. A `ProtectedPassword`
generated on your build machine will **not** decrypt on the target (assuming they are different machines). The password will be protected during the initial setup.

### Generate the protected password on the target

If the service account password has been changed, run the following on the target machine.

Run the published app with the `--protect-secret` switch, passing the plaintext
password as an argument:

```powershell
cd C:\inetpub\wwwroot\ActiveRolesDashboard
dotnet .\ActiveRolesDashboard.dll --protect-secret "<password>"
```

This prints an encrypted value and exits without starting the web host. Copy the
value into the target's `appsettings.json`:

```json
"ServiceAccount": {
  "Username": "PROD\\svc_ars",
  "ProtectedPassword": "<value printed by --protect-secret>",
  "DailyRefreshTime": "02:00",
  "LoadOnStartup": true
}
```

> The utility uses the same application name and key-ring path
> (`App_Data\DataProtectionKeys`) as the running app, so the value it produces is
> guaranteed to decrypt at runtime on that machine. Do not delete or overwrite the
> target's `App_Data\` folder, or the protected password will no longer decrypt.

### ServiceAccount settings

| Setting | Description |
| --- | --- |
| `Username` | Service account in `DOMAIN\user` form, e.g. `PROD\svc_ars`. |
| `ProtectedPassword` | Data Protection-encrypted password, generated on the target. |
| `DailyRefreshTime` | 24-hour `HH:mm` time for the daily superset cache refresh. |
| `LoadOnStartup` | When `true`, the shared cache is built at application startup. |

## Startup cache warm-up and Entra group membership

The shared superset is collected once by a background service using the service
account, then refreshed daily at `DailyRefreshTime` (and on-demand when an Active
Roles admin triggers a manual refresh). It is served to every user and projected
per-user according to their Active Roles delegation, so the expensive collection
runs once rather than on every login.

Collection happens in two phases:

1. **Base superset** (AD/Entra totals, KPIs, permission model). As soon as this is
   published the "Building cache…" overlay clears and the dashboard renders. This
   is the minimum time a user waits on first sign-in after a restart.
2. **Entra group membership enrichment** (one Active Roles search per group; this
   is the dominant cost). It runs in the background *after* the base superset is
   published, loading in batches of `ActiveRoles:EntraMembershipBatchSize` groups.

Because membership loads after the dashboard is already visible:

- A user who signs in **after** membership finishes sees group-based Entra KPIs and
  drilldowns immediately, with no progress badge.
- A user who signs in **while** membership is still loading sees the dashboard right
  away plus a header progress badge that counts down as the server loads membership.
  The page refreshes automatically once loading completes. No per-session loading is
  performed — the shared collection drives the badge.

> The first collection after a restart takes longer than base-data-only did in
> earlier builds, because membership is now pre-loaded once into the shared cache
> instead of lazily per session. The overlay still only blocks on the base superset,
> not on membership.

## First-time deploy checklist

1. Ensure the ASP.NET Core 9 Hosting Bundle is installed on the target.
2. Create the IIS site and application pool on the target.
3. Deploy with config seeding:

   ```powershell
   .\deploy\Publish-ToServer.ps1 -Target <server> -AppPool <pool> -IncludeAppSettings
   ```

4. Open a browser window and navigate to the site. The setup wizard will be triggered.The first sign-in will trigger the initial cache collection, which may take several minutes. The dashboard will display a "Building cache…" overlay until the base superset is ready.
5. Complete the setup wizard. This will trigger the initial cache collection, which may take several minutes. 
6. Sign in to the application. The dashboard will display a "Building cache…" overlay until the base superset is ready.

## Subsequent deploys

For repeat deploys, omit `-IncludeAppSettings` so the target's config and Data
Protection keys are preserved:

```powershell
.\deploy\Publish-ToServer.ps1 -Target <server> -AppPool <pool>
```

## Troubleshooting

| Symptom | Likely cause | Resolution |
| --- | --- | --- |
| Files on the target are not updated | `-RemotePath` points to the wrong folder | Confirm the site path (e.g. `inetpub\wwwroot\ActiveRolesDashboard`) and pass `-RemotePath` if needed. |
| `--protect-secret` starts the web host instead of prompting | Running an older binary that predates the fix | Redeploy the current build; the switch is handled before the host starts. |
| Cache never becomes ready / auth fails | `ProtectedPassword` was generated on a different machine | Regenerate it **on the target** with `--protect-secret` and update `appsettings.json`. |
| Entra group KPIs empty / progress badge stays up for a while after a restart | Membership is still loading in the background into the shared cache | Expected on first collection; the badge counts down and the page refreshes when done. Increase `ActiveRoles:EntraMembershipBatchSize` to load in larger batches. |
| App pool cannot be stopped/started remotely | WinRM not enabled or insufficient rights | Enable remote PowerShell on the target or stop/start the pool manually. |
| Robocopy fails (exit code 8+) | Locked files or share/permission issues | Ensure the app pool is stopped and you have admin access to `\\<Target>\C$`. |
