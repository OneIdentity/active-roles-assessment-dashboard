# KPI and category definitions

KPI, category, chart, dataset and drilldown definitions are loaded in layers at startup:

1. **Built-in definitions** &mdash; `Definitions/*.definitions.json`, embedded in the assembly.
   They ship with every release, cannot be changed at runtime and are always the fallback.
2. **Overrides** &mdash; `App_Data/Definitions/*.definitions.json` (under the content root).
   Files are read in alphabetical order and merged on top of the built-in definitions.
   An item replaces any earlier item with the same `id` (case-insensitive); new ids are added.
   To hide a built-in KPI or category, override it with `"enabled": false`.
3. **Database** (planned) &mdash; will replace the override folder as the writable layer.

## Rules

- Files must match `Definitions/definitions.schema.json`. Supported `schemaVersion`: `1`.
- The merged result is validated (references, expressions, duplicate ids). If any override
  makes the result invalid, **all** overrides are ignored and the built-in definitions are used.
  Unreadable files or files with an unsupported `schemaVersion` are skipped individually.
- Problems are logged as warnings and shown under **Settings &rarr; Definitions**.
- Overrides apply at startup only. Settings shows pending changes, validates them without
  applying, and offers a restart once they are valid.
- `App_Data` is preserved across upgrades (see [Deployment](Deployment.md)); keep override files
  minimal so new built-in definitions from later releases still take effect.

## Example

`App_Data/Definitions/local.definitions.json`:

```json
{
  "schemaVersion": 1,
  "kpis": [
	{ "kind": "Composite", "id": "SomeKpiId", "categoryId": "SomeCategory", "dataSource": "ActiveRoles", "enabled": false, "sources": [ "SomeKpiId" ] }
  ]
}
```
