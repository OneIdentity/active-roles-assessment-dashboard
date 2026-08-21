# Chart.js (self-hosted, offline)

The dashboard renders category charts using **Chart.js**, served locally so the
application has **no external/internet dependency at runtime**.

## Action required (one-time)

Download the single UMD minified bundle and place it in this folder as:

	wwwroot/js/chart.umd.min.js

### Where to get it (on a machine with internet)

- GitHub releases: https://github.com/chartjs/Chart.js/releases
  (download `chart.umd.min.js` from the release assets), or
- npm:  `npm pack chart.js`  then extract `dist/chart.umd.min.js`, or
- jsDelivr (save the file, do NOT reference the URL):
  https://cdn.jsdelivr.net/npm/chart.js@4/dist/chart.umd.min.js

Recommended version: Chart.js 4.x

## Notes

- The file is referenced by the dashboard pages as `js/chart.umd.min.js`
  **before** `js/dashboard.js`.
- Until the file is present, charts are simply not drawn; the dashboard still
  works because the initializer checks `typeof Chart === 'undefined'` and exits
  gracefully.
- Chart.js has no telemetry and requires no license key or network access once
  the file is hosted locally.
