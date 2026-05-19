# Migration Path — Seed → MVP

> Path from the current seed (Angular SPA + flat ASP.NET controller + Postgres + two nginx containers, mock playlist only) to the MVP described in `CLAUDE.md` (Caddy, Clean Architecture backend, JWT auth, upload-driven library, Range+transcode streaming, device handoff).
>
> Seven phases. Each phase ends with a **working, deployable system** — no phase breaks the app. Land each phase as its own branch/PR so regressions stay bisectable. Apple Music, HLS, and Redis are explicitly out of scope for MVP.
>
> **This file is the progress tracker.** When a phase completes, flip its heading to `✅ Done` and leave a one-line note on when it landed (merge commit + date). If the work changed any architectural fact, update `CLAUDE.md` in the same PR.

## Status

| Phase | Title | Status |
|---|---|---|
| 0 | Baseline | ✅ Done |
| 1 | Infrastructure swap | ✅ Done — merged to main 2026-04-19 (`44f2e2c`) |
| 2 | Clean Architecture skeleton + EF Core | ✅ Done — landed on phase-2-clean-arch 2026-04-20 |
| 3 | Authentication | ✅ Done — merged to main 2026-04-20 (`phase-3-auth`) |
| 4 | Library CRUD + artwork | ✅ Done — landed on main 2026-05-19 |
| 5 | Streaming upgrade | ✅ Done — merged to main 2026-05-19 (`phase-5-streaming`) |
| 6 | Playback session + device handoff | ⏳ Not started |
| 7 | Operational hygiene | ⏳ Not started |

---

## Phase 0 — Baseline (current state)

What exists today:
- `docker-compose.yml` with 4 services (nginx edge, frontend container, api, db).
- `MusicController.GetPlaylist(id)` returns a hardcoded mock.
- `ApiService` hits `http://localhost:8080/api/...`.
- No auth, no EF Core, no migrations, no streaming beyond `UseStaticFiles`.
- Sample audio/image assets live in `ColdHarbourBackend/wwwroot/assets/`.

Don't refactor anything yet — just confirm `docker compose up --build` works end-to-end and you can play both mock tracks in a browser. That's your regression baseline for every subsequent phase.

---

## Phase 1 — Infrastructure swap (Caddy, secrets, Postgres lockdown) ✅ Done

> Landed on `main` at commit `44f2e2c` (merge of `phase-1-infra-swap`), 2026-04-19. `docker compose up --build` confirmed end-to-end.


**Goal.** Same behavior as today, fewer containers, with secrets out of the repo.

**Changes.**
- Replace `nginx/` with `caddy/Caddyfile`. Caddy serves the built Angular bundle + reverse-proxies `/api/*` and `/ws/*` → api. `auto_https off` (tunnel will handle TLS later).
- Collapse the `frontend` container: the Caddy image does a multi-stage build (Node → `dist/` → `caddy:alpine` + `COPY --from=build`).
- Remove the `frontend` service from `docker-compose.yml`; rename `nginx` → `caddy`.
- Remove the `5432:5432` port mapping on `db` (Postgres stays on the compose network only).
- Move DB credentials to a gitignored `.env`; add `.env.example` with placeholders.
- Update `ColdHarbourFrontend/src/app/services/api.service.ts`: read base URL from Angular `environment`, use `/api` (relative) in production.

**Definition of done.**
- `docker compose up --build` serves the mock playlist on `http://localhost` through Caddy.
- Only three services run: `caddy`, `api`, `db`.
- `git grep -i password` returns no real credentials.

---

## Phase 2 — Clean Architecture skeleton + EF Core ✅ Done

> Landed on branch phase-2-clean-arch, 2026-04-20.

**Goal.** Backend is split into four layered projects; persistence is real. Mock endpoint behavior unchanged.

**Changes.**
- Create `ColdHarbour.sln` with four projects:
  ```
  src/ColdHarbour.Domain/
  src/ColdHarbour.Application/
  src/ColdHarbour.Infrastructure/
  src/ColdHarbour.Api/
  ```
  + three test projects. Enforce the dependency rule in `.csproj` files: `Domain` → nothing; `Application` → `Domain`; `Infrastructure` → `Application` + `Domain`; `Api` → `Application` (+ `Infrastructure` only in a `DependencyInjection.cs` wire-up extension).
- Add NuGet packages: `MediatR`, `EFCore.Npgsql`, `FluentValidation.AspNetCore`, `Serilog.AspNetCore`.
- Define **Library** context in `Domain`: `Track`, `Album`, `Artist` (plain POCOs, no attributes).
- EF Core `DbContext` in `Infrastructure` with the first migration (`0001_InitialLibrary`).
- Move `MusicController` to `Api` project. Introduce `GetPlaylistQuery` (MediatR) + handler that, **temporarily**, still returns the mock playlist — no DB reads yet. This validates the pipeline without blocking on real data.
- Wire Serilog.

**Definition of done.**
- `dotnet build ColdHarbour.sln` succeeds.
- `docker compose up` still serves the mock playlist — now through the MediatR handler.
- `dotnet ef migrations add` / `database update` runs cleanly against the compose Postgres.
- `Domain` project references nothing beyond the BCL.

---

## Phase 3 — Authentication (JWT + refresh + rate limits + tunnel trust) ✅ Done

> Landed on branch phase-3-auth, merged to main 2026-04-20.

**Goal.** Every endpoint is `[Authorize]` by default. Login/refresh works. Safe to start pointing a tunnel at it.

**Changes.**
- **Identity** context in `Domain`: `User`, `RefreshToken` (entity), `PasswordHash` (value object), `Role` enum (`Owner` | `User`).
- Migration `0002_Identity`.
- `Infrastructure` services: `PasswordHasher` (Argon2id), `TokenService` (JWT sign + refresh rotation + family revocation).
- Commands: `RegisterUser`, `AuthenticateUser`, `RefreshAccessToken`, `RevokeRefreshTokenFamily`, `Logout`. Each with a FluentValidation validator registered as a MediatR pipeline behavior.
- `Api/Controllers/AuthController`: `POST /api/auth/register` (owner-gated after the first user), `POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/logout`.
- JWT middleware + `[Authorize]` on everything except `auth/*` and `health`.
- ASP.NET `RateLimiter` registered for `login` (5/min/IP, 10/hr/user) and `refresh` (10/min/IP).
- `ForwardedHeaders` middleware trusting only the tunnel's internal IP.
- Seed logic on first startup: if no users exist, create one `Owner` from env vars `COLDHARBOUR_BOOTSTRAP_EMAIL` / `COLDHARBOUR_BOOTSTRAP_PASSWORD` (prints creds once to the log, then unsets).
- Frontend: `AuthService` (access token in memory, refresh via cookie), HTTP interceptor for `401 → refresh → retry-once`, `AuthGuard` on routes, `LoginPageComponent`.

**Definition of done.**
- Fresh install → bootstrap user → login works → access token is used → expires after 15 min → silent refresh works.
- Reusing a consumed refresh token in curl revokes the whole family (visible in DB).
- `/api/music/playlist/1` returns `401` without a token.
- No hardcoded CORS `AllowAll` in `Production` environment.

---

## Phase 4 — Library CRUD (upload-driven) + artwork ✅ Done

> Landed on `main` 2026-05-19. 125 tests pass (48 domain, 34 application, 20 integration, 23 infrastructure).

**Goal.** Users can upload, delete, and sync tracks from the frontend. Artwork extracted during upload and served via the artwork endpoint.

**Changes.**
- **Application commands:** `UploadTrack` (multipart), `DeleteTrack`, `SyncLibrary`, `PreviewLibrarySync`.
- **Services:** `TrackIngestService` (TagLibSharp extraction, canonical path write, dedup on `AudioSha1`); `LibraryReconciler` (walks disk, emits diff, applies on confirmation, Postgres advisory lock).
- Upload flow:
  1. Multipart POST → temp file at `/content/library/.tmp/{uuid}`.
  2. Extract tags; compute `AudioSha1`.
  3. If `AudioSha1` exists in DB → delete temp file, return existing track (idempotent).
  4. Else atomic rename to `/content/library/{artist}/{album}/{track}.{ext}` (sanitized names).
  5. Extract embedded art → `/content/cache/art/{artSha1}-source.{ext}`.
  6. Insert `Track` / upsert `Album` / upsert `Artist`.
- Delete flow: remove `Track` row (cascade-clean orphan `Album`/`Artist` rows if no tracks remain), delete audio file, delete per-cacheKey transcodes for this `AudioSha1`.
- Sync UI: `GET /api/library/sync/preview` returns `{ added: [...], missing: [...], renamed: [...] }`; user confirms → `POST /api/library/sync` applies.
- `GET /api/artwork/{albumId}?size=64|256|1024` — SkiaSharp generates + caches `{artSha1}-{size}.webp`. `Cache-Control: max-age=31536000, immutable`.
- Replace the mocked `GetPlaylistQuery` handler with real DB reads. Delete `wwwroot/assets/`. Move the two sample tracks into `/content/library/` via upload for continuity.
- Frontend: upload dropzone, delete button per track, sync button (shows diff, confirm dialog), `<img>` tags pointed at `/api/artwork/...`.

**Definition of done.**
- Drag-drop an MP3 in the UI → appears in the playlist → plays (still pass-through; streaming upgrade is phase 5).
- Manually drop 3 files into `/content/library/` on the host → click "Sync" → diff shown → confirm → tracks appear.
- Delete a track → file gone from disk, row gone from DB, no orphan `Album`.
- Artwork loads for every track that has embedded art or a sidecar `cover.jpg`.

---

## Phase 5 — Streaming upgrade (Range + transcode-on-demand) ✅ Done

> Merged to `main` 2026-05-19 (merge of `phase-5-streaming`). 141 backend + 43 frontend tests, all green.

**Goal.** `/api/stream/{trackId}` replaces direct asset URLs. Seeks are instant. FLAC plays on Safari.

**Changes.**
- Install FFmpeg in the api Dockerfile (`RUN apt-get install -y ffmpeg`).
- **Services:** `LocalStreamingService` (pass-through with `PhysicalFile(..., enableRangeProcessing: true)`), `TranscodeService` (FFmpeg process, keyed `SemaphoreSlim` for per-cacheKey locking, tempfile → atomic rename, `RequestAborted` cancellation), `DeviceCapabilityService` (records per-device codec support).
- `RegisterDevice` command extended: client sends `supportedCodecs: []`, `preferredProfile`, `bitrateCap?`.
- Profile selection: if `track.codec ∈ device.supportedCodecs` and no bitrate cap → `original`; else cheapest matching profile from `{opus-128, aac-192, mp3-192}`.
- `GET /api/stream/{trackId}?profile=...` (profile param is optional; default comes from device registration). ETag = `cacheKey`, `Cache-Control: max-age=31536000, immutable`.
- Frontend: `AudioSource` interface with `LocalAudioSource` implementation (wraps `HTMLAudioElement`, targets `/api/stream/...`). Device registration runs on login; codec probe via `MediaSource.isTypeSupported`.
- `CachePruneJob` (weekly, `COLDHARBOUR_TRANSCODE_CACHE_LIMIT_BYTES` default 5 GB, LRU).

**Definition of done.**
- Playing a FLAC in Safari triggers an Opus transcode; second play is instant.
- Scrubbing a 40-minute track is responsive (Range works).
- Killing the tab mid-transcode kills the FFmpeg process (check with `ps`).
- Two browser tabs hitting the same uncached transcode run FFmpeg exactly once.

---

## Phase 6 — Playback session + device handoff

**Goal.** "Now playing" is a property of the account, not the tab. "Play here" works.

**Changes.**
- **Playback** context in `Domain`: `PlaybackSession` (aggregate per user), `Device` (entity), `PlayEvent`.
- Migration `0003_Playback` for `Device` and `PlayEvent` (both persisted). `PlaybackSession` lives in-memory via `InMemoryPlaybackSessionStore` (`ConcurrentDictionary<UserId, PlaybackSession>` behind an `IPlaybackSessionStore` port).
- `PlaybackSessionHub` (SignalR or raw WebSocket on `/ws/playback`). JWT auth on handshake, close code `4001` on token expiry → client refreshes and reconnects.
- Commands: `RegisterDevice`, `TransferPlayback`, `UpdatePlaybackPosition` (heartbeats every 2s from the active device). Material events (`PlayStarted`, `PlayEnded`, `TrackCompleted`) persist `PlayEvent` rows.
- Frontend: `PlaybackSessionService` (WebSocket client), `DevicesPageComponent` (list devices + "play here" button), session-aware player that shows "Playing on {otherDevice}" when this tab isn't active.

**Definition of done.**
- Log into the same account in two browser profiles. Both show each other in the Devices list.
- Click "Play here" on profile B while profile A is playing → A stops, B resumes from A's position.
- Restart the api container — material play history (yesterday's plays) is intact; current session position is lost (acceptable for MVP).

---

## Phase 7 — Operational hygiene (backups, stats, headers)

**Goal.** Safe to leave running unattended for weeks.

**Changes.**
- `BackupJob` — weekly `pg_dump --format=custom` → `/content/backups/coldharbour-{YYYYMMDD}.backup.gz`, retain 4 latest.
- `ArtCachePruneJob` — LRU on `cache/art` at the `COLDHARBOUR_ART_CACHE_LIMIT_BYTES` cap.
- `PlaybackStatsJob` — weekly aggregates materialized to a read model.
- `IntegrityCheckJob` — sample 5% of tracks/week, re-hash audio bytes, flag divergence (DB column) without auto-deleting.
- `RefreshTokenSweepJob` — nightly purge of expired/revoked tokens.
- Security headers in the Caddyfile: `Strict-Transport-Security`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, a CSP that permits only self + the artwork/stream endpoints (no external origins needed while Apple Music is post-MVP).
- `GET /api/health` returns `{ status, db, cacheSize }`.

**Definition of done.**
- First Saturday after deploy: a backup file shows up.
- Cache dirs stay under their caps under synthetic load.
- Health endpoint is reachable without auth; everything else isn't.
- `curl -I` on any endpoint returns the expected security headers.

---

## Post-MVP bridge (not this migration)

Once the MVP is boring, the doors that are already designed open:

- **Apple Music adapter** — `AppleMusicAudioSource` on the client, `AppleMusicAdapter` + `MusicKitDeveloperTokenRotationJob` on the server, `ProviderLink` rows for per-user credentials, `Track.Provider = 'apple_music'`, CSP relaxed to allow MusicKit origins.
- **Redis session store** — swap `InMemoryPlaybackSessionStore` → `RedisPlaybackSessionStore` if multi-instance api or restart-durable sessions become real requirements.
- **HLS** — add if mobile-on-flaky-network pain materializes.
- **Playlists beyond the implicit "all tracks" view** — `Playlist` aggregate, M3U8 import/export.

Each of these is one contained change because the seams already exist.

---

## Per-phase checklist (use on every PR)

See `CLAUDE.md` § "Post-implementation checklist" — apply to the feature landed in each phase. The bar is: the phase's definition of done is met, and the checklist passes against the code delivered.
