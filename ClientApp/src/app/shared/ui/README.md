# IRIS design-system usage

This app consumes the One Identity **IRIS** design system directly from the
official published npm packages so that the dashboard stays visually identical
to Active Roles. **Do not** hand-copy token CSS or re-draw icon SVGs — update
the package version instead.

## Source of truth

| Artifact | Package | How it's used |
|----------|---------|----------------|
| Design tokens (`--oi-*` CSS custom properties: colors, spacing, radius, sizes, themes) | [`@oneidentity/iris-ui-tokens`](https://pkgs.dev.azure.com/1id/_packaging/OneIdentity/npm/registry/) | Imported globally in [`src/styles.scss`](../../../styles.scss) |
| Icons (named SVG strings, `currentColor`-driven) | [`@oneidentity/iris-ui-icons`](https://pkgs.dev.azure.com/1id/_packaging/OneIdentity/npm/registry/) | Consumed by [`iris-icon`](./iris-icon/iris-icon.component.ts) via the package `manifest` |

Both packages are published to the restricted OneIdentity Azure Artifacts
registry (configured via `.npmrc`).

## Components

The IRIS component library itself is a private **React** app and cannot be
imported into this Angular project. The shared controls under this folder
(`iris-button`, `iris-text-input`, `iris-form-field`, `iris-icon`, …) are
**faithful Angular ports** that consume the token/icon packages above, so they
render identically to their React counterparts.

When adding a control, port the matching IRIS component's markup, class names
and token usage rather than inventing a bespoke design.

## Using icons

```html
<iris-icon name="MagnifyingGlass" size="20px"></iris-icon>
<iris-icon name="Trash" size="16px" title="Delete"></iris-icon>
```

Icon names come from `@oneidentity/iris-ui-icons` (e.g. `MagnifyingGlass`,
`AddressBook`). Omit `title` for decorative icons (rendered `aria-hidden`).

## Updating the design system

```powershell
npm update @oneidentity/iris-ui-tokens @oneidentity/iris-ui-icons
```

Review the package CHANGELOGs after updating, as token values or icon names may
change between releases.
