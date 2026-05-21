# Frontend Maturation Path — Seed → Full Product

> Path from today's frontend (login + single playlist page, inline brutalist CSS, no shared component layer) to a full-product Angular app with a documented design-system kit and every page the MVP needs: dashboard, library, account, owner-gated user creation, full-screen player, plus 404 and error pages.
>
> Seven phases. Each phase ends with a **working, deployable system** — no phase breaks the app. Land each phase as its own branch/PR so regressions stay bisectable.
>
> **TDD is mandatory** (see `CLAUDE.md` § "Working agreement"). Every component in every phase is preceded by a failing Jasmine/Karma spec. No "tests after."
>
> **Branch + commit per phase.** Each phase happens on its own `frontend-phase-N-<slug>` branch and lands as one PR. The post-implementation checklist in `CLAUDE.md` applies to every phase.
>
> **This file is the progress tracker.** When a phase completes, flip its heading to `✅ Done` and leave a one-line note (merge commit + date). If the work changed any architectural fact, update `CLAUDE.md` in the same PR.

## Status

| Phase | Title | Status |
|---|---|---|
| 0 | Baseline | ✅ Done |
| 1 | Docs + foundation (Button, Input, FormField) | 🚧 In progress (`frontend-phase-1-foundation`) |
| 2 | Kit completion (Card, Modal, Badge) + refactor existing pages | ☐ Not started |
| 3 | Home dashboard `/home` | ☐ Not started |
| 4 | Library page `/library` (all tracks) | ☐ Not started |
| 5 | Account `/account` + owner-gated `/create-account` | ☐ Not started |
| 6 | Full-screen player `/player` | ☐ Not started |
| 7 | Error handling (`/not-found`, `/error`, global handler) | ☐ Not started |

---

## Phase 0 — Baseline (current state)

What exists today:

- Two pages: `LoginPageComponent` (`/login`), `PlaylistPageComponent` (`/playlist/:id`). A third route `/devices` exists for device handoff.
- `/ → /playlist/1` redirect.
- One inline `.btn` / `.btn--primary` rule in `src/styles.scss`. Every other UI element is styled inline in its component's SCSS.
- No `shared/ui/` layer — only `shared/icons/` (play, pause SVGs).
- The sync-preview modal in playlist-page, the login form fields, and the "PLAYING" / "THIS DEVICE" badges are all duplicated/inlined.
- Test setup: Jasmine/Karma, `MusicListComponent` has a working spec to mirror.

Don't refactor anything yet — just confirm `npm test` and `npm run build` are green on `main`. That's your regression baseline.

---

## Phase 1 — Docs + foundation (Button, Input, FormField) 🚧 In progress

> Branch: `frontend-phase-1-foundation`.

**Goal.** Ship the documentation infrastructure (this file lives in `docs/`, `MIGRATION.md` lives in `docs/`, `CLAUDE.md` has the reusable-component policy). Ship the first three shared components (`Button`, `Input`, `FormField`) under `src/app/shared/ui/`. Migrate the login page to consume them so the kit has at least one real consumer.

**Changes.**

- Move `MIGRATION.md` → `docs/MIGRATION.md`. Add this file. Update `CLAUDE.md`:
  - New "Shared component kit" subsection under "Design patterns" — kit lives in `src/app/shared/ui/`, every reusable UI element goes there first, components are standalone + signal-input + token-driven + spec-covered.
  - Fix the `MIGRATION.md` reference to `docs/MIGRATION.md`.
  - Add "**Frontend progress.** `docs/FRONTEND_MIGRATION.md` tracks the frontend maturation phases."
- Create `src/app/shared/ui/` with `index.ts` barrel and short `README.md` pointing at the kit catalogue + this roadmap.
- **`ButtonComponent`** — `src/app/shared/ui/button/button.component.{ts,html,scss,spec.ts}`. Inputs (signal): `variant: 'default' | 'primary' | 'danger'` (default `default`), `size: 'sm' | 'md'` (default `md`), `type: 'button' | 'submit'`, `disabled`, `loading`. Emits `(click)`. Standalone, OnPush.
- **`InputComponent`** — `src/app/shared/ui/input/input.component.{ts,html,scss,spec.ts}`. Implements `ControlValueAccessor` so it composes with `formControl` / `formControlName`. Inputs: `type` (`text|email|password|search|number`), `placeholder`, `autocomplete`, `disabled`.
- **`FormFieldComponent`** — `src/app/shared/ui/form-field/form-field.component.{ts,html,scss,spec.ts}`. Projects a control via `<ng-content>`. Inputs: `label`, `errorMessage` (string or null), `hint`. Renders the existing brutalist "label above input + error below" layout from login-page.
- Migrate `LoginPageComponent` to use the kit. Delete the inline `.field`, `.field-label`, `.field-input`, `.field-error`, `.submit-btn` styles from `login-page.component.scss`. Keep the page chrome (card, header, "COLD HARBOUR" title) as-is. Page must look identical.
- `.btn` rules in `src/styles.scss` stay temporarily, with a comment noting they're legacy and will be removed when Phase 2 refactors the remaining consumers (playlist, devices, player).

**TDD discipline (per component).**

1. Write a Jasmine spec that asserts the desired behavior. Run it; confirm red.
2. Implement the minimum production code to make it pass. Confirm green.
3. Refactor with the spec as safety net.

Tests cover the public API only: rendered classes for variants, propagation of disabled, slot projection, event emission, `ControlValueAccessor` write/read. Don't test private internals.

**Definition of done.**

- `cd ColdHarbourFrontend && npm test -- --watch=false` — all existing tests pass, plus the three new specs.
- `npm run build` — clean, no new warnings.
- `docs/FRONTEND_MIGRATION.md` and `docs/MIGRATION.md` resolve correctly; `git grep -n "MIGRATION.md" CLAUDE.md` returns the updated `docs/MIGRATION.md` path.
- `git grep -nR "from '.*features" ColdHarbourFrontend/src/app/shared/ui` is empty — kit has no upward dependencies.
- Login page renders and works identically — manual smoke: load `/login`, submit empty (see field errors), submit invalid, submit valid bootstrap creds → reach `/playlist/1`.
- `ng serve --proxy-config proxy.conf.json` starts cleanly; existing pages still render.

---

## Phase 2 — Kit completion (Card, Modal, Badge) + refactor existing pages

> Branch: `frontend-phase-2-kit-complete`.

**Goal.** Finish the shared kit. Migrate every page that has an inline equivalent so the legacy `.btn` global rule can be deleted.

**Changes.**

- **`CardComponent`** — `src/app/shared/ui/card/`. Bordered container. Inputs: `padding: 'sm' | 'md' | 'lg'`, `shadow: boolean` (the 6px black offset shadow used on the login card). Slots: `[slot=header]`, `[slot=footer]`, default for body.
- **`ModalComponent`** — `src/app/shared/ui/modal/`. Inputs: `isOpen` (signal), `title`. Emits `(close)`. Backdrop click and ESC dismiss. Focus trap. Projects body content via `<ng-content>`, optional `[slot=footer]` for action buttons.
- **`BadgeComponent`** — `src/app/shared/ui/badge/`. Inputs: `variant: 'default' | 'active' | 'accent'`. Slot for label text.
- Refactor `PlaylistPageComponent` sync-preview overlay → `<app-modal>` + `<app-button>`. Delete the inline `.sync-overlay`, `.sync-dialog` styles.
- Refactor `DevicesPageComponent` → `<app-card>`, `<app-badge>` for "PLAYING"/"THIS DEVICE" pills, `<app-button>` for "PLAY HERE".
- Refactor `MusicListComponent` "PLAYING" badge → `<app-badge variant="active">`.
- Refactor `PlaylistPageComponent` header buttons (`+ ADD TRACKS`, `SYNC LIBRARY`, `DEVICES`) → `<app-button>`.
- Refactor `PlayerComponent` transport buttons → `<app-button>` where appropriate (icon-only buttons stay).
- **Delete** the `.btn` / `.btn--primary` rules from `src/styles.scss` once every consumer is migrated. Verify with `git grep "btn--primary"` and `git grep "class=\"btn"`.

**Definition of done.**

- All existing tests still pass; three new specs (Card, Modal, Badge) pass.
- `git grep -n "\\.btn[ \"']" ColdHarbourFrontend/src` returns only the new component's internal references (no legacy `.btn` usage outside `shared/ui/button/`).
- Visual smoke test: playlist sync modal opens, dismisses on backdrop click, ESC, and the close button. Devices page badges match previous appearance. Player transport unchanged.
- Kit catalogue in `src/app/shared/ui/README.md` lists all six components with one-line summaries.

---

## Phase 3 — Home dashboard `/home`

> Branch: `frontend-phase-3-home`.

**Goal.** Replace the `/ → /playlist/1` redirect with a proper authenticated landing page. **Use the `frontend-design` skill** to produce the first cut so the page has the polish a dashboard needs and isn't just a list of boxes.

**Changes.**

- `HomePageComponent` at `src/app/features/home/pages/home-page/`. Uses `Card`, `Button`, `Badge` exclusively for UI.
- Route: `{ path: 'home', component: HomePageComponent, canActivate: [authGuard] }`. Update the empty path redirect: `/ → /home`.
- Sections (initial cut, can grow): recently played strip (last 8 tracks from `PlayEvent`), "jump back in" album grid, library stats (track count, total duration, last sync timestamp).
- New backend dependency surface: a `GET /api/home/summary` endpoint may be needed. If so, this phase grows; otherwise compose from existing queries. **Defer to the implementer** to decide and document in the PR.
- Use the `frontend-design` skill to generate the first HTML/SCSS pass; then make it real (wire signals, replace placeholder data with `ApiService` calls, ensure tokens and shared components are used). Skill output is a starting point, not a finished page.

**TDD note.** Build the component shell + spec first; iterate the visual design on top.

**Definition of done.**

- `/home` loads after login; `/` redirects to `/home`.
- Page uses only shared kit components for UI primitives.
- Spec covers: renders for an authenticated user, shows loading state while data fetches, renders recently-played items.
- Mobile reflow works (single column under 768px).

---

## Phase 4 — Library page `/library` (all tracks)

> Branch: `frontend-phase-4-library`.

**Goal.** A library-wide flat view of every track, with search and sort. The existing `/playlist/:id` page stays (it's the implicit "all tracks" playlist for now); `/library` becomes the navigation entry point for browsing.

**Changes.**

- `LibraryPageComponent` at `src/app/features/library/pages/library-page/`. Route: `{ path: 'library', component: LibraryPageComponent, canActivate: [authGuard] }`.
- Search input (`<app-input type="search">`) filtering by `title | artist | album`, signal-derived.
- Column sort (Track / Artist / Album / Duration / Added). Local sort over the in-memory list; backend pagination is post-MVP.
- Reuse `MusicListComponent` (it already takes its data from `MusicService`). If a query-driven variant is needed, lift the filtered list into a signal and pass via input.
- Add a nav element (header bar or sidebar) so `/home`, `/library`, `/devices`, `/account` are reachable. Keep brutalist — `<app-button>` row works.
- Update `ApiService` only if a `GET /api/library/tracks` endpoint is needed and doesn't already exist; otherwise reuse the playlist 1 query.

**Definition of done.**

- `/library` loads after login. Search filters live. Column sort toggles asc/desc.
- Selecting a track plays it via the existing `MusicService` flow — playback bar updates.
- Spec covers: empty state, filtered list reflects search input, sort changes order.

---

## Phase 5 — Account `/account` + owner-gated `/create-account`

> Branch: `frontend-phase-5-account`.

**Goal.** A real account surface plus the existing-but-unused owner-only registration form.

**Changes.**

- `AccountPageComponent` at `src/app/features/account/pages/account-page/`. Route: `{ path: 'account', component: AccountPageComponent, canActivate: [authGuard] }`.
  - Sections: profile (email, role), change password form (`<app-form-field>` × 3), device list (`<app-card>` + `<app-badge>`), logout-everywhere button (`<app-button variant="danger">`).
  - Change-password command exists on the backend (or needs `POST /api/auth/change-password`). Document in the PR if a new endpoint is required.
- `CreateAccountPageComponent` at `src/app/features/account/pages/create-account-page/`. Route: `{ path: 'create-account', component: CreateAccountPageComponent, canActivate: [authGuard, ownerGuard] }`.
  - `ownerGuard` — new file `src/app/core/auth/owner.guard.ts`. Reads `user.role` from `AuthService`; redirects to `/home` if not `Owner`.
  - Form: email, password (with confirm), role (`User` only — the bootstrap Owner is the only Owner).
  - Backend `POST /api/auth/register` is already owner-gated. This page is the frontend half.
- Add nav entries for `/account`. `/create-account` is linked from the `/account` page conditionally (`if role === Owner`).

**Definition of done.**

- `/account` shows current user, allows password change (server returns success), lists devices.
- `/create-account` renders for an Owner, 403s for a User (redirect via guard).
- Creating a user succeeds against the existing backend endpoint.
- Specs cover: account page renders, owner-guard redirects non-owner, create-account submits.

---

## Phase 6 — Full-screen player `/player`

> Branch: `frontend-phase-6-fullscreen-player`.

**Goal.** A dedicated `/player` route — deep-linkable, browser-back works. The existing mini-player at the bottom of `PlaylistPageComponent` becomes a click-target that navigates to `/player`.

**Changes.**

- `PlayerPageComponent` at `src/app/features/player/pages/player-page/`. Route: `{ path: 'player', component: PlayerPageComponent, canActivate: [authGuard] }`.
- Layout: huge album art (uses `--accent` from `ColorService`), track title (`var(--font-headline)` display-xl), artist, large transport (play/pause/prev/next/scrub), volume, queue list, current device indicator.
- Reuse `AudioService`, `MusicService`, `ControllerService`, `PlaybackSessionService` — no new playback state.
- Mini-player in `PlaylistPageComponent` gets a click handler: `router.navigate(['/player'])`. Close button on `/player`: `Location.back()`.
- Keyboard shortcuts (Space for play/pause, arrows for seek) registered via `ControllerService` while `/player` is active.
- MediaSession metadata: already handled by `ControllerService`; verify it covers the new route.

**Definition of done.**

- Clicking the mini-player navigates to `/player`. Pressing back returns.
- `/player` is deep-linkable: pasting the URL in a fresh tab (already authenticated) lands on the page; if a track was active server-side, the WS session pre-fills it.
- All transport works. Scrubbing seeks. Volume persists across navigation.
- Spec covers: renders current track from `MusicService`, transport buttons dispatch correct service calls.

---

## Phase 7 — Error handling (`/not-found`, `/error`, global handler)

> Branch: `frontend-phase-7-errors`.

**Goal.** No more blank screens. Routing failures and uncaught exceptions land on a brutalist error page.

**Changes.**

- `NotFoundPageComponent` at `src/app/features/errors/pages/not-found-page/`. Route: `{ path: '**', component: NotFoundPageComponent }` — added last in the routes array.
  - Layout: large "404" headline, short message, `<app-button>` back to `/home`.
- `ErrorPageComponent` at `src/app/features/errors/pages/error-page/`. Route: `{ path: 'error', component: ErrorPageComponent }`. Accepts `?code=<string>&message=<string>` query params.
- `GlobalErrorHandler` at `src/app/core/errors/global-error-handler.ts`, registered via `provideErrorHandler` in `app.config.ts`. On uncaught error:
  - Logs via Serilog/console.
  - Navigates to `/error?code=UNEXPECTED&message=<truncated>`.
  - Excludes `HttpErrorResponse` — those are handled by the existing HTTP interceptor.
- `HttpErrorInterceptor` extension: on 5xx, navigate to `/error?code=SERVER`. On 4xx (non-401, since 401 already triggers refresh), surface to the calling component via the existing error signal pattern.

**Definition of done.**

- Navigating to `/nonexistent` shows the 404 page.
- Throwing a synthetic error from a component triggers the global handler → `/error` page renders with the code.
- Backend down (`docker stop coldharbour-api`) → next nav surfaces the `/error` page rather than a blank.
- Specs cover: 404 renders, error page reads query params, global handler navigates on uncaught error.

---

## Post-phase bridge (not this migration)

Once these seven phases land:

- **Playlists beyond "all tracks"** — when the backend grows the `Playlist` aggregate, the existing `PlaylistPageComponent` already takes an ID, so adding a sidebar of user playlists is a routing + nav change, not a rewrite.
- **Mobile-first refinements** — most pages already reflow at 768px; the next pass adds touch gestures, swipe-to-dismiss for modals, and a bottom tab bar.
- **Theming beyond `--accent`** — light/dark switch lives entirely in CSS tokens; no component touches.
- **Apple Music UI affordances** — provider-link cards on `/account`, provider badges on tracks. The kit already has `Badge` and `Card`; this is wiring.

Each of these is a contained change because the kit and the routing seam are now in place.

---

## Per-phase checklist (use on every PR)

See `CLAUDE.md` § "Post-implementation checklist" — apply to the feature landed in each phase. The bar is: the phase's definition of done is met, and the checklist passes against the code delivered.
