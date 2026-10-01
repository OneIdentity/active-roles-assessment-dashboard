# IRIS Login — Visual Spec (Iteration 1)

Derived from the UX team's React/Vite prototype (`ARS_Dashboard_IRIS/src/iris-shell`).
The prototype has **no Login page**, so this spec reconstructs the Login look from the
authoritative IRIS **design tokens** and the prototype's **form-control / button**
components. The Angular Login (built on `@oneidentity/iris-ui-tokens`) must match these.

> Source of truth: `src/iris-shell/src/tokens/*.css`, `styles/typography.css`,
> `components/{Button,TextInput,FormField}/*.module.css`.

## Design tokens (naming convention: `--oi-*`)
These ship as CSS custom properties via `@oneidentity/iris-ui-tokens`. Values below are
the **light theme** (`tokens.light.css`) + primitives (`tokens.primitives.css`).

### Typography
- Font family (default): `"Inter", system-ui, sans-serif` — `--oi-font-family-default`
- Font family (code): `"IBM Plex Mono", monospace`
- Body (primary UI text): 14px / 20px / 400 — `--oi-text-body-s-*`
- Label / table header (semibold): 14px / 20px / 600 — `--oi-text-body-s-strong-*`
- Caption (labels, errors): 12px / weight 400 — `--oi-text-caption-*`
- Page title (Heading 04): 20px / 600 — `--oi-text-heading-04-*`

### Colour (light theme)
- Brand / primary action: `#00A6F4` — `--oi-base-color-brand`, `--oi-button-background-primary`
  - hover/active: `#0084D1`
- Page background: `#FFFFFF` (`--oi-background-color-primary`); secondary `#F1F5F9`
- Content primary text: `#0F172B`; secondary `#314158`; tertiary (placeholder/hint) `#62748E`
- Border default `#CAD5E2`; muted `#E2E8F0`; strong `#90A1B9`
- Focus ring: brand (`--oi-border-color-focus` → `--oi-base-color-brand`)
- Error: `--oi-base-color-error` `#FB2C36`; error text `--oi-color-red-600`

### Spacing / sizing / radius
- Spacing scale: xs 4 · s 8 · m 12 · l 16 · xl 24 · xxl 32 · xxxl 48 (`--oi-spacing-*`)
- Control heights: s 24 · default 32 · l 40 (`--oi-size-*`)
- Radius: s 2 · default 4 · l 8 · max 50vh (`--oi-border-radius-*`)
- Motion: short 120ms, default 200ms; ease `cubic-bezier(0.23, 1, 0.32, 1)`

## Components

### Button (primary)
- `display:inline-flex; align-items/justify:center; gap:--oi-button-gap-default`
- `padding-inline: --oi-spacing-m`; `border-radius: --oi-border-radius-default (4px)`
- `border: 1px solid transparent`; font = body-s **strong** (14/20/600)
- Height: default `--oi-button-size-height-default` (32px); `l` = 40px (use `l` for the
  Login submit button to match a prominent CTA)
- Primary variant: bg `#00A6F4`, text `--oi-content-color-constant` (#FFF), `box-shadow:--oi-shadow-low`
  - hover `#0084D1`, active `#0084D1`
- Disabled: bg `--oi-button-background-disabled` (#CAD5E2), text `--oi-content-color-disabled`
- Transition: background/border/color `120ms` ease

### TextInput (wrapper + input)
- Wrapper: `inline-flex; width:100%; gap:--oi-button-gap-s`; bg `--oi-background-color-primary`
- `border-radius:4px`; `padding-inline:--oi-spacing-s (8px)`; `border:1px solid --oi-border-color-muted`
- Hover: bg `--oi-background-color-secondary`
- Focus-within: `outline:1.5px solid --oi-border-color-focus; outline-offset:-1.5px`
  (border goes transparent, no box-shadow)
- Invalid: `border-color:--oi-color-red-500`; invalid+focus outline red
- Heights: s 24 · default 32 · l 40 (use **l/40px** for Login fields to pair with the CTA)
- Input text = body-s (14/20/400); placeholder = `--oi-content-color-tertiary`
- Supports leading/trailing `.icon` (tertiary colour) — e.g. user/lock icons

### FormField (label + control + error)
- `display:flex; flex-direction:column; gap:--oi-spacing-s (8px)`
- Label: body-s **strong** (14/20/600), colour `--oi-content-color-primary`
- Required marker: tertiary colour, normal weight
- Error text: caption (12px), colour `--oi-color-red-600`, below the control

## Login layout (reconstructed)
- Centered card on page background; vertical stack of FormFields (`gap:--oi-spacing-l/16px`).
- Order: brand logo/title → Username field → Password field → (optional) error → primary CTA (full-width, size `l`).
- Card: bg `--oi-background-color-primary`, `border-radius:--oi-border-radius-l (8px)`,
  subtle shadow, padding `--oi-spacing-xl`/`xxl`.
- Title uses Heading 04 (20/600).

## Notes / open items (to confirm when building step 7)
- Exact Login card width, logo asset, and "remember me"/error affordances are **not** in the
  prototype; we will design these to the tokens and **prompt** if a dedicated IRIS component
  (e.g. Checkbox, Toast) is needed beyond tokens.
- Dark / high-contrast themes exist (`tokens.dark.css`, `tokens.hc-*`); iteration 1 targets light.
