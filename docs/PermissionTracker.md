# Permission Tracker

The Permission Tracker (**Tools > Permission Tracker**) shows who has which rights in your
environment. It reports two sources side by side:

- **Active Directory** – the native ACLs (`nTSecurityDescriptor`) on directory objects.
- **Active Roles** – the delegation defined by Access Template Links.

Entra ID support is planned for a later release.

## Access

Requires the **Use Permission Tracker** permission (granted by default to Dashboard
Administrators and Auditors). Active Roles administrators always have access. See
[RolesAndPermissions.md](RolesAndPermissions.md).

Active Directory data is read with **your own** Active Roles session, so you only see
the ACLs you are allowed to read. Access Template Links are read with the service
account because end-user tokens cannot read the Active Roles configuration.

## Running a scan

| Option | Description |
| --- | --- |
| Base DN | Where to start. Leave empty to scan every managed domain. |
| Active Directory / Active Roles | Which sources to include. At least one is required. |
| All objects | Off (default): containers and key security objects only. On: every object, which is much slower. |

Scans run in the background; you can leave the page and come back. Each user can run one
scan at a time, and results are kept in memory for `ResultRetentionMinutes`. They are
not saved to disk and are lost when the application restarts.

- **Confirmation** – if a scan would read more objects than `ConfirmationThreshold`, it
  pauses and asks you to confirm before reading permissions.
- **Limit** – a scan never reads more than `MaxObjects`. Results above the limit are
  marked **Truncated**.
- **Cancel** – stops the scan and keeps whatever was collected so far (partial results).
- Objects whose permissions could not be read are listed with the error instead of
  failing the scan.

## Viewing results

Each source has a **tree** view (objects by directory hierarchy) and a **table** view.
Filters apply to both views and to exports:

| Filter | Effect |
| --- | --- |
| High-risk only | Shows only rights such as Full Control, Write DACL, Write Owner, Reset Password and other sensitive extended rights. |
| Hide inherited | Hides entries inherited from a parent container. |
| Hide built-in | Hides well-known built-in trustees (for example SYSTEM, Domain Admins, Enterprise Admins). |

## Export

Completed or cancelled scans can be exported to **Excel**, **PDF** or **Word** using the
buttons below the filters. The export contains the currently filtered results, a summary
per source, the list of objects that could not be read and a note if the scan was
truncated. CSV export is not available.

## Configuration

Optional `PermissionTracker` section in `appsettings.json`:

| Setting | Default | Description |
| --- | --- | --- |
| `MaxConcurrentScans` | `2` | Scans that may run at the same time across all users. Further scans wait. |
| `ParallelReadsPerScan` | `4` | Concurrent security-descriptor reads per scan. |
| `ConfirmationThreshold` | `5000` | Object count above which the user must confirm the scan. |
| `MaxObjects` | `50000` | Hard limit on objects per scan. |
| `ResultRetentionMinutes` | `60` | How long finished scan results are kept in memory. |

```json
"PermissionTracker": {
  "MaxConcurrentScans": 2,
  "ConfirmationThreshold": 5000
}
```
