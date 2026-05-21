# Shared UI kit

Reusable, design-system-aligned Angular components used across the app. Every component here is:

- standalone, `OnPush`, signal-input based
- styled with tokens from `src/styles.scss` only (no hardcoded colors/borders/fonts)
- preceded by a Jasmine spec (TDD; see `CLAUDE.md` § "Working agreement")
- free of upward dependencies (no imports from `features/*` or `core/*`)

See `docs/FRONTEND_MIGRATION.md` for the phased plan that introduces and consumes each component.

## Catalogue

| Component | Selector | Purpose |
|---|---|---|
| `ButtonComponent` | `<app-button>` | Brutalist button with `default | primary | danger` variants, `sm | md` sizes, `loading` state. |
| `InputComponent` | `<app-input>` | Native text input wrapper implementing `ControlValueAccessor` for reactive forms. |
| `FormFieldComponent` | `<app-form-field>` | Label + projected control + error/hint wrapper. |
| `CardComponent` | `<app-card>` | Bordered container with `sm | md | lg` padding, optional drop shadow, header/body/footer slots. |
| `ModalComponent` | `<app-modal>` | Overlay dialog with `isOpen` input + `(close)` output. Backdrop click and ESC dismiss; body + footer slots. |
| `BadgeComponent` | `<app-badge>` | Inline pill with `default | active | accent` variants. |

## Conventions

- One folder per component: `{name}/{name}.component.{ts,html,scss,spec.ts}`.
- Re-export from `./index.ts` (and update its type re-exports).
- Add the component to the catalogue table above.
- Spec coverage: variant rendering, disabled/loading state, event emission, CVA read/write where applicable.
