# Library Browse + Edit + Playlists Migration

> Adds **Album browse**, **Artist browse**, **real persistent Playlists**, **album-cover editing**, and **in-place song-metadata editing** to ColdHarbour — backend + frontend. Follows the same phased-branch, TDD-first style as [`WS_PROTOCOL_HARDENING_MIGRATION.md`](./WS_PROTOCOL_HARDENING_MIGRATION.md) and [`PLAYBACK_HARDENING_MIGRATION.md`](./PLAYBACK_HARDENING_MIGRATION.md). This file is the single source of truth for progress; mark each phase `✅ Done` as it merges to `main`.

## Decisions (locked with user)

- **Playlists are per-user private.** Each playlist has `OwnerUserId`. Cross-user access returns **404** (not 403) — never disclose existence. Sharing is deferred.
- **Metadata edit is in-place only.** Edit title / track number / album title / year / artist name as direct field edits. **No** track→album/artist re-parenting in this migration.
- **Cover edit = file upload.** Validate MIME + magic bytes + size; write to `cache/art/{sha1}-source.{ext}`; set `Album.CoverArtSha1`; regenerate thumbnails. **Never write into `library/`.** No URL fetch (SSRF avoidance).
- External-API metadata enrichment is explicitly **out of scope** (future want).

## Workflow

Per the phase-branch workflow: each phase branches **from `main`** → TDD `red → green → refactor` → validate (build + all tests + lint) → PR → merge to `main`. The next phase branches from the freshly-merged `main`. Frontend phases use the **`frontend-design` skill** and must align to [`DESIGN.md`](./DESIGN.md) (4px borders, no rounded corners, `var(--accent)`, correct fonts) and reuse `shared/ui/`. 90% min coverage; strict TDD.

---

## Status

| Phase | Branch | Scope | Status |
| ----- | ------ | ----- | ------ |
| 1 | `album-artist-read` | Browse backend: read repo + queries + DTOs + Albums/Artists controllers; ArtworkController ETag fix | ✅ Done |
| 2 | `album-artist-browse-ui` | Browse frontend: album/artist list + detail pages, routes, nav | ⬜ Todo |
| 3 | `library-edit-backend` | Track/Album/Artist field mutators + update commands + PATCH endpoints + cover upload endpoint | ⬜ Todo |
| 4 | `library-edit-ui` | Edit modals (song metadata, album metadata) + cover upload UI | ⬜ Todo |
| 5 | `playlists-backend` | Playlist aggregate + per-user repo + EF migration + CQRS + controller + real GetLibrary query | ⬜ Todo |
| 6 | `playlists-ui` | Playlist pages + PlaylistService + cut over library off the fake GetPlaylistQuery | ⬜ Todo |

---

## Phase 1 — `album-artist-read` (browse backend)

**Goal:** read-only album & artist browse endpoints, plus fix the artwork ETag cache-busting hazard.

**Tests first (Application + Api integration):**
- `ILibraryReadRepository` returns albums (id, title, artist name, year, coverArtSha1, trackCount) and a single album with its ordered tracks; returns artists with album counts and a single artist with its albums. Empty-library cases.
- `GET /api/albums` → list; `GET /api/albums/{id}` → detail with tracks; `404` for unknown id.
- `GET /api/artists` → list; `GET /api/artists/{id}` → detail with albums; `404` for unknown id.
- ArtworkController: ETag includes `CoverArtSha1` so a changed cover invalidates caches; unknown album → 404.

**Implementation:**
- Extend `ILibraryReadRepository`: `GetAlbumsAsync`, `GetAlbumAsync(Guid)`, `GetArtistsAsync`, `GetArtistAsync(Guid)` returning read models. Implement in `LibraryReadRepository` (EF projections, no entity leakage).
- DTOs: `AlbumSummaryDto`, `AlbumDetailDto` (with `MusicDto[]`), `ArtistSummaryDto`, `ArtistDetailDto` (with `AlbumSummaryDto[]`).
- Queries + validators: `GetAlbumsQuery`, `GetAlbumQuery(Guid)`, `GetArtistsQuery`, `GetArtistQuery(Guid)`.
- `AlbumsController`, `ArtistsController` (`[Authorize]`).
- **ArtworkController fix:** ETag becomes `"{albumId}-{size}-{coverArtSha1}"`; image URLs in DTOs carry `?v={coverArtSha1}` so a new cover busts the immutable cache. (Hurdle: current ETag `"{albumId}-{size}"` never changes when the cover changes.)

**Done when:** browse endpoints green, ETag test green, doc row → ✅.

---

## Phase 2 — `album-artist-browse-ui` (browse frontend) · frontend-design skill

**Goal:** users can browse albums and artists and drill in.

**Tests first (Jasmine/Karma, host-component + HttpTestingController):**
- `ApiService`: `getAlbums`, `getAlbum(id)`, `getArtists`, `getArtist(id)` hit correct URLs and map camelCase DTOs.
- Album-list page renders cards, loading + empty states; click navigates to detail.
- Album-detail renders track list (reuse `MusicListComponent`); play wires to existing playback (`setQueue`).
- Artist-list / artist-detail equivalents.

**Implementation:**
- `ApiService` types + methods (Observables die here).
- Pages: `AlbumsPageComponent` `/albums`, `AlbumDetailPageComponent` `/albums/:id`, `ArtistsPageComponent` `/artists`, `ArtistDetailPageComponent` `/artists/:id`. Routes in `app.routes.ts`.
- Reuse `shared/ui/` (`card`, `button`) and `MusicListComponent` for track lists; album cards use `/api/artwork/{albumId}?size=256&v={sha1}`.
- Add nav entries (CONTROL ROOM on home + nav) for ALBUMS / ARTISTS.
- Loading/error/empty signals; `isLoading()` / `error()`.

**Done when:** all four pages tested + green, design-aligned, doc row → ✅.

---

## Phase 3 — `library-edit-backend` (edit backend)

**Goal:** persist in-place metadata edits and album-cover uploads. App still never writes to `library/`.

**Tests first (Domain + Application + Api integration):**
- Domain mutators: `Track.UpdateMetadata(title, trackNumber)` (validation: non-empty title, trackNumber ≥ 0); `Album.UpdateMetadata(title, year)`; `Artist.Rename(name)`. Invalid inputs throw.
- `Album.UpdateCoverArt(sha1)` already validates 40-char lowercase hex — add coverage if missing.
- Commands + validators: `UpdateTrackCommand`, `UpdateAlbumCommand`, `RenameArtistCommand`, `UpdateAlbumCoverCommand`.
- `IArtworkService.SaveSourceAsync(stream, contentType)` validates MIME + **magic bytes** + size cap, writes `cache/art/{sha1}-source.{ext}`, regenerates `64/256/1024` webp, returns sha1. Rejects non-image / oversize / mismatched-magic-bytes.
- Integration: `PATCH /api/tracks/{id}`, `PATCH /api/albums/{id}`, `PATCH /api/artists/{id}`, `POST /api/albums/{id}/cover` (multipart). Auth required; unknown id → 404; invalid payload → 400.

**Implementation:**
- Add mutators to `Track`/`Album`/`Artist` (keep factories; only widen what's needed).
- `ITrackRepository`/album/artist repos: add update persistence (`SaveChangesAsync` boundary).
- MediatR handlers; FluentValidation validators at edge.
- Controllers: PATCH endpoints + cover POST. Cover handler: read stream → `ArtworkService.SaveSourceAsync` → `Album.UpdateCoverArt(sha1)` → save. Never trust client `Content-Type`; sniff magic bytes.

**Done when:** mutator + command + endpoint tests green, magic-byte rejection tested, doc row → ✅.

---

## Phase 4 — `library-edit-ui` (edit frontend) · frontend-design skill

**Goal:** edit UI for song metadata + album metadata + cover upload.

**Tests first (Jasmine/Karma):**
- `ApiService`: `updateTrack`, `updateAlbum`, `renameArtist`, `uploadAlbumCover(id, File)` — correct verbs/URLs/bodies (multipart for cover).
- Edit-song modal: pre-fills fields, validates, emits save, reflects loading/error.
- Edit-album modal: fields + cover file picker; preview; save.
- After save, the detail view refreshes (new `?v={sha1}` on cover busts cache).

**Implementation:**
- Reuse `shared/ui/` `Modal`, `FormField`, `Input`, `Button`. Add edit affordances on album-detail and track rows.
- Cover upload: `<input type=file accept=image/*>`, client-side size hint, POST multipart; on success re-fetch album with new sha1.
- Design-aligned (brutalist modal, 4px borders, `steps(1)` transitions).

**Done when:** modals tested + green, cover round-trips and visibly updates, doc row → ✅.

---

## Phase 5 — `playlists-backend` (real playlists)

**Goal:** replace the fake "playlist = whole library" with a real per-user Playlist aggregate.

**Tests first (Domain + Application + Api integration):**
- `Playlist` aggregate (`Id`, `OwnerUserId`, `Name`, `CreatedAt`, ordered `PlaylistTrack` children with `Position`): `Create`, `Rename`, `AddTrack` (append at end), `RemoveTrack` (re-pack positions), `ReorderItems(orderedTrackIds)` (whole-order replacement, mirrors `setQueue`; rejects unknown/duplicate ids). Position invariant `0..n-1` contiguous.
- `IPlaylistRepository`: per-user scoping — `GetForUserAsync(userId)`, `GetAsync(id, userId)` returns null for non-owner (→ 404 at controller).
- EF migration `AddPlaylists` (playlist + playlist_track tables, FK + Position, cascade delete). Restores cleanly into empty Postgres.
- CQRS: `CreatePlaylistCommand`, `RenamePlaylistCommand`, `DeletePlaylistCommand`, `AddTrackToPlaylistCommand`, `RemoveTrackFromPlaylistCommand`, `ReorderPlaylistCommand`; `ListPlaylistsQuery`, `GetPlaylistQuery(Guid)` (real, per-user). Validators.
- Integration: `PlaylistsController` CRUD; non-owner → 404; unknown track → 400.
- New real library query: `GET /api/library/tracks` (`GetLibraryQuery`) returns the whole library as `MusicDto[]` — the honest replacement for what the fake playlist endpoint did.

**Implementation:**
- Domain aggregate + child entity; repository (one per aggregate root); EF config + migration.
- Handlers + validators; controller with `[Authorize]`, owner-scoped lookups returning 404.
- Keep old `GetPlaylistQuery(int)` temporarily until Phase 6 cuts the frontend over.

**Done when:** aggregate + per-user 404 + migration tests green, doc row → ✅.

---

## Phase 6 — `playlists-ui` + library cutover · frontend-design skill

**Goal:** playlist UI and removal of the fake-playlist scaffolding.

**Tests first (Jasmine/Karma):**
- `PlaylistService` (signals): list/create/rename/delete/add/remove/reorder; `ApiService` methods hit correct URLs.
- Playlist-list page + playlist-detail page (reorder via up/down, remove, play → `setQueue`).
- "Add to playlist" affordance on track rows / album detail.
- `MusicService.loadLibrary()` now calls `GET /api/library/tracks` (real) instead of `getPlaylist(1)`.

**Implementation:**
- `PlaylistService` + `ApiService` methods. Pages `/playlists`, `/playlists/:id` (reintroduce route — now backed by a real feature). Nav entry.
- **Cutover:** `MusicService` reads `/api/library/tracks`; remove `LIBRARY_PLAYLIST_ID` magic constant. Remove the fake `GetPlaylistQuery(int)` + its `MusicController` route on the backend once the frontend no longer calls it.
- Reconcile CLAUDE.md "no playlist page" note since playlists now exist.
- Design-aligned reorder controls (brutalist, keyboard parity per checklist).

**Done when:** playlist CRUD + reorder tested + green, library no longer depends on fake playlist, docs reconciled, doc row → ✅.

---

## Cross-cutting checklist (every phase)

- DTO ≠ entity; validators at the edge; `[Authorize]` on new endpoints.
- Per-user playlist access → **404** for non-owners.
- Cover upload: MIME + magic-byte + size validation; writes only to `cache/`; ETag/`?v=` busts cache.
- Frontend types match DTOs (camelCase/nullability); loading + error states; design-system compliant; keyboard parity; no absolute origins; proxy/Caddy untouched (no new WS).
- Schema changes restore into empty Postgres; no secrets committed.
- Reconcile this doc + CLAUDE.md when behavior diverges.
