# Localization

The Active Roles Dashboard supports a fully localized UI. This document explains the localization
architecture, the resource file conventions, how language is resolved at runtime, and the steps to
add a new language or translate a new string.

## Supported languages

Languages are defined centrally in `SupportedLanguage.All` (`Models/DashboardModels.cs`). The
currently supported set is:

| Code | Language | Flag                 |
|------|----------|----------------------|
| `en` | English  | `img/flags/en.svg`   |
| `fr` | Français | `img/flags/fr.svg`   |
| `it` | Italiano | `img/flags/it.svg`   |
| `es` | Español  | `img/flags/es.svg`   |
| `de` | Deutsch  | `img/flags/de.svg`   |

English (`en`) is the default culture (`SupportedLanguage.DefaultCode`). Only languages listed in
`SupportedLanguage.All` are honoured by the request culture pipeline.

## How the display language is resolved

Request culture is configured in `Program.cs` via `UseRequestLocalization` and driven by the custom
`UserSettingsRequestCultureProvider` (`Services/UserSettingsRequestCultureProvider.cs`). Precedence,
highest first:

1. **Authenticated user's saved language** — read from the user's `usersettings` (`UserSettings.Language`).
   Once a user is signed in, their saved language is the single source of truth.
2. **Culture cookie** — for unauthenticated requests only. The login page language selector writes the
   standard ASP.NET Core culture cookie (`.AspNetCore.Culture`) so a visitor can choose a language before
   signing in without affecting the authenticated-user precedence model.
3. **Configured default** — `ActiveRoles:DefaultLanguage` from `appsettings.json`.
4. **Framework fallback** — English.

On successful login, `LoginModel.OnPostAsync` aligns the culture cookie with the newly authenticated
user's saved language so the destination dashboard renders correctly immediately after the redirect.

## Resource file conventions

Resource files live under `Resources/` and mirror the namespace/path of the type or view they back.

### Page views (`IViewLocalizer`)

A Razor page `Pages/<Name>.cshtml` that injects `IViewLocalizer` reads from:

```
Resources/Pages/<Name>.resx        (neutral / English)
Resources/Pages/<Name>.fr.resx
Resources/Pages/<Name>.it.resx
Resources/Pages/<Name>.es.resx
Resources/Pages/<Name>.de.resx
```

### Page models (`IStringLocalizer<TModel>`)

A page model class `<Name>Model` that injects `IStringLocalizer<<Name>Model>` reads from a **separate**
resource set named after the model type:

```
Resources/Pages/<Name>Model.resx
Resources/Pages/<Name>Model.fr.resx
...
```

> **Important:** The view (`<Name>.resx`) and the model (`<Name>Model.resx`) are distinct resource sets.
> A key defined for the view is **not** visible to the model localizer and vice versa. If a localizer
> renders the raw key text (e.g. `LoadingData` instead of a translation), the key is almost always
> missing from the resource set that particular localizer targets.

### Shared partials

Shared partials under `Pages/Shared/` follow the same convention under `Resources/Pages/Shared/`, e.g.
`Resources/Pages/Shared/_DashboardHeader.resx`.

### Shared KPI / category / chart metadata (`KpiResources`)

Static dashboard metadata (category names, KPI labels, chart titles) is localized at read-time through
`KpiLocalizer`, backed by the shared `Resources/KpiResources.resx` set. Relevant models expose
localized properties rather than plain strings:

- `CategoryInfo.DisplayName` → `Cat_{Key}`
- `KpiInfo.DisplayName` / `TileLabel`
- `ChartInfo.Title` → `Chart_{Key}` (falls back to the initialized literal when no resource exists)

## JavaScript strings

Text that is set from client-side JavaScript cannot use `@Localizer[...]` directly at the point of use.
Two patterns are used to keep JS text localized:

- **Serialized literals / page-local i18n objects** — the server serializes localized values into a small
  JS object (e.g. `loginI18n`) using `System.Text.Json.JsonSerializer.Serialize(...)` to avoid quoting bugs.
- **`data-*` attributes** — an element emits a localized value in a data attribute that the script reads at
  runtime. For example, `_ExportDialog.cshtml` emits `data-exporting-text` on the export form, which
  `dashboard.js` reads for the "Exporting data..." overlay. English literals are kept as JS fallbacks.

### Overlays after authentication

Loading overlays are rendered on the destination page, which resolves in the authenticated user's culture,
so their `<p>` text is localized with the page's `LoadingDashboardData` key. The login page is a special
case: because the user's language is not known until authentication completes, `LoginModel.OnPostAsync`
localizes the post-login overlay message **in the authenticated user's culture** server-side and returns it
to the client as JSON, so the overlay reflects the user's language rather than the login screen's language.

## Adding a translation for a new string

1. Add the key/value to the **neutral** resource file (`<Name>.resx` or `<Name>Model.resx`) with the English text.
2. Add the same key with the translated value to each culture sibling (`.fr`, `.it`, `.es`, `.de`).
3. Reference the key in the view (`@Localizer["Key"]`) or model (`_localizer["Key"]`).
4. For JS-visible text, emit the value via a serialized literal or a `data-*` attribute (see above).
5. Build the solution — the `.resx` files are compiled and invalid XML fails the build.

## Adding a new language

1. Add a new entry to `SupportedLanguage.All` in `Models/DashboardModels.cs` (code, display name, flag image).
2. Add the flag SVG under `wwwroot/img/flags/<code>.svg`.
3. For every existing resource set, add a `<Name>.<code>.resx` sibling containing translations for all keys.
4. Register the culture in the supported cultures list in `Program.cs` if cultures are enumerated explicitly.
5. Build and verify the language appears in the Settings and login-page language selectors.

## Troubleshooting

- **A string shows the raw resource key** (e.g. `LoadingData`): the key is missing from the resource set the
  localizer targets. Confirm whether the caller uses `IViewLocalizer` (view `.resx`) or
  `IStringLocalizer<TModel>` (model `.resx`) and add the key to the correct set.
- **A string is always English regardless of language**: the value may be a hardcoded literal rather than a
  localized lookup, or the resource exists only in the neutral file with no culture siblings.
- **The build fails with `MSB3103: Invalid Resx file`**: the `.resx` XML is malformed — check that every
  `<data>`/`<resheader>` element is well-formed and closed.
