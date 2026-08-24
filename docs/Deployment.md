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

## Prerequisites

| Requirement | Notes |
| --- | --- |
| ASP.NET Core 9 Hosting Bundle (on the target) | Required because the app is published framework-dependent. |
| .NET 9 SDK (on the build machine) | Required to run `dotnet publish`. |
| IIS site + application pool (on the target) | The app pool name is passed to the script. |
| Remote PowerShell / WinRM to the target | Used to stop/start the app pool remotely. |
| Administrative share access (`\\<Target>\C$`) | Used by `robocopy` to copy files. |

## The deploy script

```powershell
# Normal repeat deploy (preserves the target's appsettings.json + Data Protection keys):
.\deploy\Publish-ToServer.ps1 -Target winemeaapp04 -AppPool ActiveRolesDashboard

# First-time deploy, seeding appsettings.json:
.\deploy\Publish-ToServer.ps1 -Target winemeaapp04 -AppPool ActiveRolesDashboard -IncludeAppSettings
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
generated on your build machine will **not** decrypt on the target. You must
generate the value **on the target** after the first deploy.

### Generate the protected password on the target

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

## First-time deploy checklist

1. Ensure the ASP.NET Core 9 Hosting Bundle is installed on the target.
2. Create the IIS site and application pool on the target.
3. Deploy with config seeding:

   ```powershell
   .\deploy\Publish-ToServer.ps1 -Target <server> -AppPool <pool> -IncludeAppSettings
   ```

4. On the target, generate the protected password and paste it into
   `appsettings.json`:

   ```powershell
   dotnet .\ActiveRolesDashboard.dll --protect-secret "<password>"
   ```

5. Set `ServiceAccount:Username` (and any environment-specific `ActiveRoles`
   settings) in the target's `appsettings.json`.
6. Restart the application pool (the script does this automatically on subsequent
   deploys).

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
| App pool cannot be stopped/started remotely | WinRM not enabled or insufficient rights | Enable remote PowerShell on the target or stop/start the pool manually. |
| Robocopy fails (exit code 8+) | Locked files or share/permission issues | Ensure the app pool is stopped and you have admin access to `\\<Target>\C$`. |
