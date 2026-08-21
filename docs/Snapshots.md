# Active Roles Dashboard — Snapshots Reference

[← Back to README](../README.md)

This document describes the **Snapshots** feature

A **snapshot** is a point-in-time capture of every dashboard KPI count, organized by **dashboard → category → KPI**. Snapshots let you record the environment's posture, compare two points in time (or a saved baseline against live data), and view KPI trends across all captures.

> Source of truth: [`Models/SnapshotModels.cs`](../Models/SnapshotModels.cs) (snapshot, comparison and trend models) and [`Services/SnapshotService.cs`](../Services/SnapshotService.cs) (capture, persistence, trend and comparison logic). The UI is [`Pages/Snapshots.cshtml`](../Pages/Snapshots.cshtml) / [`Pages/Snapshots.cshtml.cs`](../Pages/Snapshots.cshtml.cs). KPI definitions live in [`Models/DashboardModels.cs`](../Models/DashboardModels.cs).
>
> See also the companion [`Assessments.md`](Assessments.md) and [`Exposure.md`](Exposure.md) — all three read the same `DashboardSummary` KPIs.

---

## 1. What a snapshot captures

A snapshot (`Snapshot`) has a **header** plus a hierarchy of dashboards, categories and KPIs.

### Header (`SnapshotHeader`)

| Field | Meaning |
|-------|---------|
| `SchemaVersion` | Snapshot file schema version (currently `1`). |
| `Id` | Unique id (GUID, `N` format). Also the file name. |
| `Label` | Optional user-supplied label (trimmed; `null` if blank). |
| `CreatedUtc` | Capture timestamp (UTC). |
| `CreatedBy` | The signed-in user who captured it. |
| `Environment` | The Active Roles Web Interface URL the data came from. |
| `KpiCount` | Total number of KPI data points captured (informational). |

Headers can be listed on their own for the snapshot table.

### Body

The body mirrors the dashboard metadata hierarchy:

- `SnapshotDashboard` (`Key`, `DisplayName`) →
- `SnapshotCategory` (`Key`, `DisplayName`) →
- `SnapshotKpi` (`Key`, `DisplayName`, `Count`, `Error`, `IsRiskKpi`)

Capture rules (`SnapshotService.Capture`):

- Iterates `DashboardInfo.All`, then `CategoryInfo.ForDashboard`, then `KpiInfo.ForCategory`.
- **Risk categories are skipped** (`IsRiskCategory`) because they are aggregations of KPIs owned elsewhere.
- Each KPI count comes from `summary.GetKpiResult(key)`; any collection error is stored in `Error`.
- Empty categories/dashboards (no KPIs) are omitted.
- `IsRiskKpi` marks KPIs where an **increase** means a **worse** posture — this drives comparison colouring.

---

## 2. Storage

- Snapshots are serialized as **indented JSON files**, one per snapshot, named `{Id}.json`.
- The folder comes from `ActiveRolesConfig.SnapshotDirectory`; if blank it defaults to **`App_Data/Snapshots`**. Relative paths are resolved under the content root.
- `SaveAsync` creates the directory if needed and writes the file.
- `ListAsync` returns **headers only** (newest first) and silently skips unreadable/corrupt files.
- `LoadAsync` / `Delete` resolve the file by id. `ResolvePath` guards against **path traversal** (rejects ids containing `/`, `\`, `..`, or invalid file-name characters).

---

## 3. Capturing a snapshot

The **Capture** action (`OnPostCaptureAsync`) runs a **fresh** dashboard query (not the cached summary) so the snapshot reflects current data, then saves it with the current user and Web Interface URL.

- An optional **label** can be supplied.
- If Entra group-membership data is still pending, the status message appends the `EntraMembershipPendingWarning` so you know membership-dependent KPIs may be incomplete.

---

## 4. Comparison

Comparisons are built on `OnGetAsync` when query parameters are supplied (`FromId`, `ToId`), via `BuildComparisonIfRequestedAsync`:

- **Saved vs saved** — both `FromId` and `ToId` are snapshot ids.
- **Saved vs current (live)** — `ToId = "current"` is a reserved value. The page runs a fresh query, captures it as an in-memory "Current" snapshot, and compares (`ToIsCurrent = true`).

### How rows are computed (`Compare`)

The comparison is keyed by **KPI key** (case-insensitive). For each KPI on the baseline (`from`) side, matched against the `to` side:

| Direction | Condition |
|-----------|-----------|
| `Increase` | `to` count > `from` count |
| `Decrease` | `to` count < `from` count |
| `NoChange` | counts equal |
| `Removed` | KPI exists only on the `from` side (`to` count is null) |
| `Added` | KPI exists only on the `to` side |

`Change` = `to - from` (negative for removed rows). Headline tallies (`IncreaseCount`, `DecreaseCount`, `NoChangeCount`, `AddedCount`, `RemovedCount`) are accumulated across all rows.

### Sentiment (colouring)

Sentiment (`DetermineSentiment`) applies only to **risk KPIs** (`IsRiskKpi`):

- Risk KPI **Increase** → **Bad**
- Risk KPI **Decrease** → **Good**
- Everything else (non-risk KPIs, no change, added/removed) → **Neutral**

This lets the UI colour a rising risk metric red and a falling one green, while leaving informational metrics neutral.

---

## 5. Trend

`BuildTrendAsync` builds a time-series across **all** saved snapshots:

- Loads every snapshot, orders **oldest first**, and uses `CreatedUtc` (`yyyy-MM-dd HH:mm`) as the shared `Labels` axis.
- Indexes each snapshot's KPI counts by key for aligned lookups.
- Builds the dashboard/category/KPI hierarchy from the **latest** snapshot (so display names are current), then fills each KPI series with **one value per snapshot label**.
- A series value is **null** where the KPI was absent in that particular snapshot, so charts can show gaps rather than false zeros.
- `IsRiskKpi` is carried on each series for consistent colouring.

`LoadAllOrderedAsync()` (the oldest-first loader used by `BuildTrendAsync`) is also reused
by the [Exposure](Exposure.md) page to build its **derived** exposure trend — Exposure
recomputes a per-technique level for each of these snapshots rather than persisting its own
history.

---

## 6. Page handlers

`SnapshotsModel` (a `DashboardPageModel`) exposes:

| Handler | Purpose |
|---------|---------|
| `OnGetAsync` | Loads the snapshot list, builds a comparison if requested, and builds the trend. |
| `OnPostCaptureAsync(label)` | Captures and saves a new snapshot from a fresh query. |
| `OnPostDeleteAsync(id)` | Deletes a snapshot by id. |

Bindable query properties: `FromId`, `ToId` (both `SupportsGet`). A `StatusMessage` (`TempData`) reports capture/delete results.

---

## 7. Relationship to Assessments and Exposure

| | Snapshots | Assessments | Exposure |
|--|-----------|-------------|----------|
| Purpose | Point-in-time KPI record + trend | Scored security/compliance posture | ATT&CK visibility / attack-surface view |
| Unit | Dashboard → category → **KPI count** | Rule → KPI, warn/fail thresholds | Technique → KPI(s), medium/high thresholds |
| Output | Raw counts, deltas, time-series | Pass / Warn / Fail, **Score & Grade** | Per-technique exposure level |
| Persistence | Saved JSON files, listable, deletable | Saved runs, history, compare | Computed live; history **derived from these snapshots** |
| Compare | Saved-vs-saved and saved-vs-current | Saved-vs-saved and saved-vs-current | Saved-vs-saved and saved-vs-current (recomputed) |
| Data source | `DashboardSummary` KPIs | The same KPIs | The same KPIs |

The Assessments compare feature is modelled directly on this snapshot comparison design (baseline/compare selectors, a reserved `current` live mode, and delta/sentiment rows). Exposure goes one step further and *reuses the saved snapshots themselves* (via `LoadAllOrderedAsync()` and `BuildFromSnapshot(...)`) to derive its compare and trend without adding any exposure-specific storage.
