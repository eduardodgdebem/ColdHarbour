# ColdHarbour

> Self-hosted music player. Phases 0–6 are complete and running. This document describes the **actual current architecture** plus the intended direction for anything still pending. When code and doc diverge, the code is authoritative — but the divergence must be reconciled (either update this doc or realign the code).

---

## Design system

The visual design is defined in [`DESIGN.md`](./DESIGN.md) and is named **Sonic Brutalism**. All UI work must align with it.

Key rules from that document:

- **Neo-Brutalism aesthetic** — raw, print-first, unapologetic. No gradients, no soft shadows, no rounded corners.
- **Palette:** Stark Black (`#000000`), Off-White (`#F9F9F9`), Acidic Yellow / `--accent` (`#D3F000` default, overridden per-album by `ColorService`).
- **Typography:** Archivo Narrow (headlines, uppercase), Public Sans (body), Space Mono (labels/metadata).
- **Borders:** 4px solid black on all interactive elements and primary containers.
- **`--accent` CSS variable** is set dynamically from the dominant color of the current album art via `ColorService` (Web Worker). Default is `#D3F000`. Every accent-colored element reads `var(--accent)`.

---

## Working agreement (read this first)

**TDD is mandatory. No exceptions.** Every piece of production code is preceded by a failing test. The loop is always `red → green → refactor`:

1. Write a failing test that expresses the desired behavior.
2. Run it; confirm it fails for the right reason.
3. Write the minimum production code to make it pass.
4. Refactor with the test as a safety net.

Rules:

- No production code without a test that drove it into existence. "I'll add tests after" is not acceptable.
- Every PR includes the tests written first. Commit order should reflect this (test commit before impl commit when practical).
- Bug fixes follow the same loop: reproduce with a failing test, then fix.
- xUnit + FluentAssertions on the backend (one test project per layer: `ColdHarbour.Domain.Tests`, `ColdHarbour.Application.Tests`, `ColdHarbour.Api.IntegrationTests`). Jasmine/Karma on the frontend.
- Integration tests hit a real Postgres (Testcontainers), never mocks of `DbContext`.
- Prefer behavior-level tests over mock-heavy unit tests. A test that breaks when internals change but behavior doesn't is a liability.

**Progress tracking.** `docs/MIGRATION.md` is the single source of truth for backend phase progress. **Frontend progress.** `docs/FRONTEND_MIGRATION.md` tracks the frontend maturation phases (shared component kit + new pages). **Playback authority.** `docs/PLAYBACK_MIGRATION.md` tracks the migration from browser-driven playback to server-authoritative (Spotify-Connect-style) playback. When a phase completes in any of these files, mark it `✅ Done` there and update this document if the phase changed any architectural fact described here.

---

## Architecture overview

ColdHarbour is a self-hosted music server that runs entirely in Docker on the user's machine. It is exposed to the public internet over a tunnel (Cloudflare Tunnel / Tailscale Funnel / similar), so the threat model is **not** "trusted LAN."

```
┌─────────────┐   ┌──────────┐   ┌───────────┐
│    caddy    │──▶│   api    │──▶│ postgres  │
│  (reverse   │   │ (ASP.NET │   │           │
│   proxy +   │   │  .NET 10)│   │           │
│   static)   │   └──────────┘   └───────────┘
└─────────────┘          │
       ▲                 ▼
       │          ┌────────────────────┐
   browser /      │  content library   │
   mobile client  │  (host bind mount) │
                  └────────────────────┘
```

Three processes, one host:

- **caddy** terminates HTTP on port 80, serves the built Angular bundle as static files, and reverse-proxies `/api/*` → api and `/ws/*` → api (WebSocket upgrade).
- **api** owns domain logic, auth, provider integration, streaming, and the raw WebSocket playback hub.
- **db** stores catalog, users, playback history, refresh tokens. Audio files live on a bind-mounted volume, never in the DB.

Three cross-cutting design axes:

1. **Server-owned playback session.** `PlaybackSession` (one per user) lives in-memory on the server. Clients publish/subscribe over WebSocket; the server arbitrates which device is active. Material events (`PlayStarted`, `PlayEnded`) are persisted as `PlayEvent` rows.
2. **Provider abstraction.** A `Track` has a provider (`local`, `apple_music`, …). Local tracks stream through the backend; third-party tracks play on the client via the provider's SDK — the backend never proxies DRM'd audio.
3. **DDD + Clean Architecture on the backend.** Dependencies point inward: `Api → Application → Domain` and `Infrastructure → Application → Domain`. The domain has no framework references.

**MVP scope.** The provider abstraction is preserved as an architectural seam, but v1 ships with a single provider (`local`) only. Apple Music integration is explicitly deferred until after the MVP is running end-to-end.

**Library management is upload-driven, not folder-driven.** The primary path is: user uploads a track through the frontend → api writes it to `library/` at a canonical path → extracts metadata → inserts the DB row. `POST /api/library/sync` is a secondary path for reconciling files dropped directly onto the mount. Sync reports a diff and applies it on confirmation. No filesystem watcher, no scheduled full scan — reconciliation is always user-triggered.

---

## Complete tech stack

**Frontend**

- Angular 21 — standalone components, **zoneless** change detection, Signals
- TypeScript 5.9
- RxJS 7.8 — used only at the HTTP boundary (Observables die at `ApiService`; everything downstream is signals)
- SCSS (design tokens from `DESIGN.md` as CSS custom properties)
- Web Worker API (`color-worker.ts` for dominant-color extraction → `--accent` CSS var)
- `MediaSession` API (OS-level transport controls)
- `HTMLAudioElement` for local playback via `LocalAudioSource` (no player libs)
- Raw WebSocket client in `PlaybackSessionService` for device handoff
- **MusicKit JS** (client-only) for Apple Music playback — loaded lazily, post-MVP

**Backend**

- .NET 10 / ASP.NET Core 10
- **MediatR** for CQRS-lite (commands return `Unit`, queries return DTOs)
- **EF Core 9 + Npgsql** for persistence
- **FluentValidation** on command/query inputs (MediatR pipeline behavior)
- **Microsoft.AspNetCore.Authentication.JwtBearer** for access tokens
- **Isopoh.Cryptography.Argon2** for password hashing (Argon2id)
- **TagLibSharp** for ID3/FLAC/OGG metadata extraction
- **FFmpeg** (shelled out via process) for on-demand transcoding
- **SixLabors.ImageSharp** (or SkiaSharp) for artwork thumbnail generation
- **`IHostedService`** for scheduled jobs (move to Hangfire when retries/dashboard become worth the dependency)
- **Serilog** for structured logging
- Swashbuckle OpenAPI — dev-only

**Explicitly _not_ in the stack:**

- **Redis** — caches are multi-MB audio/image blobs; filesystem + HTTP Range wins. `IPlaybackSessionStore` port exists to swap in Redis later if needed.
- **HLS** — cache-first whole-file transcoding is enough for single-digit users.
- **Lossless-to-lossless transcoding** — pointless bit repackaging; skipped.
- **SignalR** — raw WebSocket used at `/ws/playback`; JWT supplied as `?access_token=` query param because browser WS API cannot send custom headers.

**Infrastructure**

- Docker Compose (services: caddy, api, db)
- **Caddy** (`caddy:alpine`) — edge proxy + SPA static server
- PostgreSQL (pin a major version before going public)

**Local dev**

- `dotnet watch run` for the backend
- `ng serve --proxy-config proxy.conf.json` for the frontend — proxies both `/api/*` and `/ws/*` to `http://localhost:8080` (WebSocket upgrade included). `wsBase` is derived from `window.location` at runtime so the same build works on any host (localhost, LAN IP, tunnel domain).

---

## All environment variables

### api (ASP.NET)

| Variable                                            | Source            | Purpose                                                           |
| --------------------------------------------------- | ----------------- | ----------------------------------------------------------------- |
| `ASPNETCORE_URLS`                                   | compose           | Kestrel binding (`http://+:8080`)                                 |
| `ASPNETCORE_ENVIRONMENT`                            | compose           | `Development` / `Production`                                      |
| `ConnectionStrings__DefaultConnection`              | appsettings / env | Postgres connection string                                        |
| `COLDHARBOUR_CONTENT_ROOT`                          | compose           | Library mount point (default `/content`)                          |
| `COLDHARBOUR_TRANSCODE_CACHE_LIMIT_BYTES`           | compose           | Soft cap for `cache/transcodes/`; LRU eviction. Default 5 GB      |
| `COLDHARBOUR_ART_CACHE_LIMIT_BYTES`                 | compose           | Soft cap for `cache/art/`. Default 512 MB                         |
| `COLDHARBOUR_FFMPEG_PATH`                           | compose           | Path to `ffmpeg` binary (default `/usr/bin/ffmpeg`)               |
| `COLDHARBOUR_JWT_SIGNING_KEY`                       | `.env` (secret)   | HS256 signing key — ≥256 bits, random                             |
| `COLDHARBOUR_JWT_ISSUER`                            | compose           | `coldharbour`                                                     |
| `COLDHARBOUR_JWT_AUDIENCE`                          | compose           | `coldharbour-web`                                                 |
| `COLDHARBOUR_ACCESS_TOKEN_TTL`                      | compose           | Default `15m`                                                     |
| `COLDHARBOUR_REFRESH_TOKEN_TTL`                     | compose           | Default `14d`                                                     |
| `COLDHARBOUR_PUBLIC_ORIGIN`                         | compose           | e.g. `https://music.example.com` — CORS allowlist + cookie domain |
| `COLDHARBOUR_BOOTSTRAP_EMAIL` / `_PASSWORD`         | `.env`            | First-run owner seed (printed once to log, then unset)            |
| `COLDHARBOUR_APPLE_TEAM_ID` `[post-MVP]`            | `.env` (secret)   | Apple Developer Team ID                                           |
| `COLDHARBOUR_APPLE_KEY_ID` `[post-MVP]`             | `.env` (secret)   | MusicKit key ID                                                   |
| `COLDHARBOUR_APPLE_PRIVATE_KEY_PATH` `[post-MVP]`   | `.env` (secret)   | Path to `.p8` file                                                |
| `COLDHARBOUR_PROVIDER_CREDENTIALS_KEY` `[post-MVP]` | `.env` (secret)   | AES-256 key for per-user provider token encryption                |

### caddy

| Variable                      | Purpose                                                 |
| ----------------------------- | ------------------------------------------------------- |
| `COLDHARBOUR_PUBLIC_HOSTNAME` | Hostname the tunnel forwards to — used by the Caddyfile |

### db (Postgres)

| Variable            | Purpose                               |
| ------------------- | ------------------------------------- |
| `POSTGRES_USER`     | Replace before tunnel exposure        |
| `POSTGRES_PASSWORD` | **Rotate before exposing via tunnel** |
| `POSTGRES_DB`       | `coldharbourdb`                       |

### frontend build

No runtime env vars — the bundle is static. All URLs use relative paths (`/api`, `/ws`) so Caddy routes them. `wsBase` is computed at module load from `window.location` (not hardcoded), making the same build work on any hostname.

### Cookie security

`Secure` flag on auth cookies (`refreshToken`, `media_token`) is set to `!env.IsDevelopment()`. In dev (`ASPNETCORE_ENVIRONMENT=Development`) cookies are sent over plain HTTP, enabling LAN/mobile testing. In production they require HTTPS.

### Secrets hygiene

- Secrets never in-repo. `.env` is gitignored; mount the `.p8` as a read-only Docker secret.
- `appsettings.Production.json` may reference env vars via `${VAR}` but never contains the values themselves.

---

## Content directory structure

```
/content/
├── library/
│   ├── {artist}/
│   │   └── {album}/
│   │       ├── 01 - {track}.flac
│   │       ├── cover.jpg               # preferred UI artwork
│   │       └── album.json              # metadata override (optional)
│   └── ...
├── playlists/
│   └── {id}.m3u8
├── cache/
│   ├── transcodes/
│   │   └── {cacheKey}.{ext}            # cacheKey = sha256(AudioSha1 || profile)
│   └── art/
│       ├── {artSha1}-source.{jpg|png|webp}
│       ├── {artSha1}-64.webp
│       ├── {artSha1}-256.webp
│       └── {artSha1}-1024.webp
└── backups/
    └── coldharbour-{YYYYMMDD}.sql.gz
```

Rules:

- The app never **writes** into `library/` — all app writes go to `cache/` or `backups/`.
- `album.json`, when present, overrides ID3 tags.
- Directory names are a fallback when tags are missing or mojibake'd.

---

## Backend layout (DDD + Clean Architecture)

```
ColdHarbourBackend/
├── src/
│   ├── ColdHarbour.Domain/          # entities, VOs, domain events, interfaces
│   ├── ColdHarbour.Application/     # commands/queries, DTOs, validators, ports
│   ├── ColdHarbour.Infrastructure/  # EF Core, repositories, auth infra, jobs
│   └── ColdHarbour.Api/             # controllers, WS hub, middleware, DI root
└── tests/
    ├── ColdHarbour.Domain.Tests/
    ├── ColdHarbour.Application.Tests/
    └── ColdHarbour.Api.IntegrationTests/
```

**Dependency rule:** `Domain` → nothing. `Application` → `Domain`. `Infrastructure` → `Application + Domain`. `Api` → `Application` (+ `Infrastructure` only in `DependencyInjection.cs` extension methods).

**Bounded contexts inside `Domain`:**

- **Library** — `Track`, `Album`, `Artist`; aggregate boundary on `Album`.
- **Playback** — `PlaybackSession` (in-memory aggregate per user), `Device` (entity), `PlayEvent` (persisted).
- **Identity** — `User` (aggregate root), `RefreshToken` (entity), `PasswordHash` (VO), `Role` enum.
- **Integration** — `ProviderLink` (post-MVP), adapter ports per provider.

---

## Streaming and storage

### Audio streaming

**Endpoint:** `GET /api/stream/{trackId}?profile=...` — authorized. Resolves `Track.Id → Track.LocalPath`. URL never accepts a filesystem path.

**Two serving modes:**

- **Pass-through** — device natively decodes the codec, no bitrate cap. `PhysicalFile(path, mime, enableRangeProcessing: true)`. Kernel `sendfile`, zero CPU.
- **Transcoded** — FFmpeg converts source → content-addressed cache file → served with Range. First play warms cache (~1–3s); seeks and replays are instant.

**Transcode profiles:**

| Profile    | Codec / bitrate      | Use case                     |
| ---------- | -------------------- | ---------------------------- |
| `original` | source, no transcode | Client supports native codec |
| `opus-128` | Opus 128 kbps        | Network-efficient default    |
| `aac-192`  | AAC 192 kbps         | Safari-safe fallback         |
| `mp3-192`  | MP3 192 kbps         | Legacy playback              |

Clients advertise codec capabilities + preferred profile at `RegisterDevice` time. Server picks cheapest profile that works.

**Content-addressed cache.** Key = `sha256(AudioSha1 || profile)`. `ETag: "{cacheKey}"`, `Cache-Control: max-age=31536000, immutable`.

**Concurrency.** Keyed `SemaphoreSlim` in `TranscodeService` — only one FFmpeg process per `(AudioSha1, profile)`. Temp file `{cacheKey}.{ext}.tmp-{uuid}`, atomic rename on success, delete on cancellation.

### `media_token` cookie

`<audio src>` and `<img src>` tags can't attach `Authorization` headers. The backend mints a short-lived JWT as an `HttpOnly` cookie (`media_token`, scoped to `/api`, 8h TTL) on every login and refresh. The JWT middleware reads it via `JwtBearerEvents.OnMessageReceived` as a fallback for `/api/stream` and `/api/artwork`. The `Secure` flag follows the environment (see Cookie security above).

### Artwork

**Endpoint:** `GET /api/artwork/{albumId}?size=64|256|1024`. Generates WebP thumbnail on first request, caches to `cache/art/`, serves with `max-age=immutable`.

**Source priority:** embedded image → sidecar `cover.jpg` → deterministic placeholder.

---

## WebSocket playback hub

**Endpoint:** `GET /ws/playback?access_token={jwt}` — raw WebSocket (no SignalR).

JWT is supplied as a query param because the browser WebSocket API cannot set custom headers. On token expiry the server closes with code `4001`; the client calls `/api/auth/refresh` and reconnects.

**Server → client messages:**

- `{ type: "session", session: PlaybackSessionDto }` — broadcast after every state mutation and on initial connect.
- `{ type: "devices", devices: DeviceDto[] }` — broadcast after connect and after transfer.

**Client → server messages (all include `deviceId`):**

- `{ type: "start", deviceId, trackId }` — begin playing a track on this device.
- `{ type: "heartbeat", deviceId, positionMs }` — every 2s from the active device.
- `{ type: "pause", deviceId, positionMs }` — user paused.
- `{ type: "resume", deviceId }` — user resumed.
- `{ type: "transfer", deviceId, positionMs }` — hand off to `deviceId` at `positionMs`.
- `{ type: "stop", deviceId }` — clear session.

**Active-device guard:** `heartbeat`, `pause`, `resume`, and `stop` messages are silently dropped if the `deviceId` in the message does not match `session.ActiveDeviceId`. This prevents stale events from a previously active device from corrupting the session after a transfer.

**Transfer position rule:** when an inactive device sends `transfer` (pulling playback to itself), `positionMs` must come from the server session's last known position — not from the requesting device's local audio clock (which would be 0 if it wasn't playing). The frontend `PlaybackSessionService` enforces this.

---

## Services, jobs, and models

### api (ColdHarbour.\*)

**Domain aggregates / entities**

- `Track { Id, Title, Artist, Album, Duration, Provider, LocalPath?, Format, Bitrate, AudioSha1 }`
- `Album { Id, Title, Artist, Year, CoverArtSha1, Tracks[] }` (aggregate root for tracks)
- `Artist { Id, Name }`
- `User { Id, Name, Email, PasswordHash, Role, TotpSecret?, CreatedAt }`
- `RefreshToken { Id, UserId, DeviceId, TokenHash, ExpiresAt, RevokedAt?, FamilyId }`
- `Device { Id, UserId, Name, UserAgent, SupportedCodecs, PreferredProfile, BitrateCap?, LastSeenAt }`
- `PlaybackSession { UserId, ActiveDeviceId, TrackId, PositionMs, IsPlaying, UpdatedAt }` — in-memory, one per user
- `PlayEvent { Id, UserId, DeviceId, TrackId, StartedAt, EndedAt?, CompletedRatio? }` — persisted

**Application commands / queries**

- Commands: `RegisterUser`, `AuthenticateUser`, `RefreshAccessToken`, `Logout`, `RegisterDevice`, `StartPlayback`, `UpdatePlaybackPosition`, `TransferPlayback`, `UploadTrack`, `DeleteTrack`, `SyncLibrary`
- Queries: `GetPlaylist`, `GetActiveSession`, `ListDevices`, `PreviewLibrarySync`

**Infrastructure services**

- `TrackIngestService` — upload flow: validate, hash, extract tags (TagLibSharp), canonical path write, extract art, upsert DB rows.
- `LibraryReconciler` — sync button: walks `library/`, diffs against DB, applies on confirmation. Advisory Postgres lock while running.
- `TranscodeService` — on-demand FFmpeg, keyed `SemaphoreSlim`, content-addressed output.
- `ArtworkService` — thumbnail generation (ImageSharp/SkiaSharp), content-addressed cache.
- `LocalStreamingService` — Range-capable pass-through from `library/`.
- `TokenService` — JWT sign, refresh rotation, family revocation, `media_token` minting.
- `PasswordHasher` — Argon2id.
- `PlaybackSessionHub` — raw WebSocket handler at `/ws/playback`.
- `InMemoryPlaybackSessionStore` — `ConcurrentDictionary<Guid, PlaybackSession>` behind `IPlaybackSessionStore`.
- `DeviceRepository`, `PlayEventRepository` — EF Core repositories for persisted playback data.

**Scheduled jobs (`IHostedService`)**

- `CachePruneJob` — Thursday 03:00 — LRU eviction on `cache/transcodes`.
- `ArtCachePruneJob` — Thursday 03:15 — LRU eviction on `cache/art`.
- `PlaybackStatsJob` — Friday 03:00 — weekly aggregate materialization.
- `BackupJob` — Saturday 03:00 — `pg_dump` → `/content/backups/`, retain 4 latest.
- `IntegrityCheckJob` — Sunday 03:00 — sample-verify 5% of `AudioSha1`; flag drift, never auto-delete.
- `RefreshTokenSweepJob` — daily 04:00 — purge expired/revoked refresh tokens.

All times `America/Sao_Paulo`. Library sync is user-triggered, not scheduled.

### frontend (`ColdHarbourFrontend/src/app/`)

**Services (`providedIn: 'root'`)**

- `ApiService` — HTTP boundary; attaches `Authorization: Bearer`; handles `401 → refresh → retry once`.
- `AuthService` — login/logout; access token in memory; schedules silent refresh; `generateUUID()` fallback for non-HTTPS contexts (plain HTTP LAN access). Exposes `email` and `name` signals — `name` is optional on the auth response (`{ accessToken, userId, email, name? }`), captured when present, null otherwise.
- `MusicService` — current track + playlist signals; `selectMusic`, `nextMusic`, `previousMusic`.
- `AudioService` — delegates to `LocalAudioSource`; exposes `isPlaying`, `currentTime`, `duration`, `volume`, `ended` signals.
- `LocalAudioSource` — wraps `HTMLAudioElement`. `loadMusic(src)` is a no-op if `src` is already loaded (prevents restart on component re-mount). `cleanup()` on every track switch.
- `PlaybackSessionService` — WebSocket client; auto-connects on `accessToken()`. Heartbeats every 2s from the active device. Reacts to incoming session changes: when this device becomes active it auto-loads the track and seeks to the transferred position; when it loses active status it pauses local audio without sending a WS event back. Uses `queueMicrotask` for signal writes that originate from effects.
- `DeviceService` — `register()` (called on login), `listDevices()`, `getOrCreateDeviceId()`.
- `ColorService` — dominant-color extraction via Web Worker → `--accent` CSS custom property. Default `#D3F000`.
- `ControllerService` — keyboard shortcuts + `MediaSession` integration.

**Pages**

- `LoginPageComponent` — `/login`
- `HomePageComponent` — `/home` (and `/` redirects here) — authenticated dashboard with HARBOUR // OPEN / CARGO MANIFEST / LATEST ARRIVALS / CONTROL ROOM sections, plus a DRY DOCK empty state when the library is empty
- `PlaylistPageComponent` — `/playlist/:id` — main library view + inline upload/sync
- `DevicesPageComponent` — `/devices` — device list, PLAYING / THIS DEVICE badges, PLAY HERE button, back button (`Location.back()`)

**App layout shell (`AppComponent`)**

- Owns the viewport: a flex column at `height: 100dvh`.
- Renders the persistent `<app-player>` at the bottom, conditional on `musicService.currentMusic()`. Pages **must not** import `<app-player>` themselves — the player is global and persists across every route. When no track is selected (e.g. `/login`), the player area collapses.
- Pages render inside a scrollable `<main class="app__content">` flex-1 region. Pages use `min-height: 100%` (not `100dvh`) so they fill the content area without pushing the player off-screen. `/login` is the only exception — it uses `min-height: 100dvh` because the player is never visible when unauthenticated.

**Key frontend patterns**

- Player component uses `allowSignalWrites: true` on its music-load effect; it never writes `isPlaying` directly — `loadMusic` handles cleanup internally.
- `wsBase` is derived at module load: `${window.location.protocol === 'https:' ? 'wss' : 'ws'}://${window.location.host}`. The ng serve proxy forwards `/ws/*` with WebSocket upgrade, so this works on any hostname including LAN IPs.
- `--accent` replaces all `--yellow` references. Interactive states (hover, active track) use `var(--accent)` throughout.
- **User-facing name resolution.** When rendering a user's name, prefer `authService.name()` (first word, uppercased) → email local-part first segment split on `. _ -` → `'FRIEND'`. Avoid rendering raw email local-parts like `eduardogdebem` when a real name is available.
- **Brutalist transitions.** Animations use `transition: <prop> Xms steps(1)` — they snap, they don't ease. Easing curves are off-brand. Hover/active states often combine a translate with a hard-shadow shrink to mimic a physical mechanical press.
- **No borderless color blocks.** Hover states that swap background colour must keep the 4px black border (or the equivalent shadow box). A floating yellow rectangle without an edge violates the design.

---

## Authentication (internet-exposed)

**Tokens**

- **Access token**: JWT, HS256, ~15min TTL, claims `sub`, `role`, `jti`, `deviceId`. Held in memory; sent as `Authorization: Bearer`. Never in localStorage.
- **Refresh token**: opaque 256-bit random, `HttpOnly` + `Secure`\* + `SameSite=Strict`, scoped to `/api/auth`, ~14d TTL. Rotated on every use; reuse revokes the entire `FamilyId`.
- **Media token**: short-lived JWT in an `HttpOnly` cookie scoped to `/api`, used as auth fallback for `<audio>` and `<img>` tags.

\* `Secure` flag is `false` in `Development` so plain-HTTP LAN testing works.

**WebSocket auth**

- JWT via `?access_token=` query param. Server closes with code `4001` on expiry; client refreshes and reconnects.

**Edge hardening**

- `auto_https off` in Caddyfile (tunnel handles TLS).
- `ForwardedHeaders` middleware trusts only the tunnel's internal IP.
- Security headers in Caddyfile: `Strict-Transport-Security`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, CSP.

---

## Documented hurdles and solutions

1. **`ApiService` hardcodes `http://localhost:8080` — breaks in production.**
   Use relative `/api`; keep the absolute URL only in `environment.development.ts`.

2. **`AllowAll` CORS leaks to production.**
   In `Production`, restrict to `COLDHARBOUR_PUBLIC_ORIGIN`. Keep `AllowAll` in `Development` only.

3. **Seek on large files stalls — endpoint doesn't support Range.**
   `PhysicalFile(path, mime, enableRangeProcessing: true)`. Return `206 Partial Content`.

4. **Two concurrent requests race on the same refresh token.**
   Rotation is single-use — serializable transaction. Reuse of a consumed token revokes the entire `FamilyId`.

5. **Access token expires mid-WebSocket.**
   Server closes with code `4001`; client refreshes and reconnects.

6. **`media_token` cookie not sent — 401 on `<audio>`/`<img>` endpoints.**
   The `Secure` flag must be `false` in `Development` (plain HTTP). Cookie is `HttpOnly` and scoped to `/api`. The JWT middleware reads it via `OnMessageReceived` as a fallback for `/api/stream` and `/api/artwork`.

7. **`crypto.randomUUID` throws on plain HTTP (non-secure context).**
   `DeviceService` and `AuthService` both have a `generateUUID()` fallback using `Math.random` for non-HTTPS origins (LAN / tunnel without TLS at the edge).

8. **WebSocket connects to `ws://localhost` from mobile — wrong host.**
   `wsBase` is derived from `window.location` at runtime, not hardcoded. The ng serve proxy (and Caddy in production) forward `/ws/*` so no absolute WS URL is ever needed in source.

9. **PLAY HERE transfers but receiving device starts at position 0.**
   An inactive device calling `transferPlayback` must use `session().positionMs` (the server's last-known position), not its own `audioService.currentTime()` (which is 0 if it wasn't playing).

10. **Player component restarts audio on every navigation back to the playlist page.**
    `LocalAudioSource.loadMusic` is a no-op if the same URL is already loaded (`src === currentSrc && this.audio`). The player component's effect must not write `isPlaying` directly — `loadMusic` handles cleanup internally. Effect requires `{ allowSignalWrites: true }`.

11. **Hub accepts pause/resume/heartbeat from non-active devices after a transfer.**
    Hub guards `pause`, `resume`, `heartbeat`, and `stop` messages against `session.ActiveDeviceId`. Messages from a device that is no longer active are silently dropped.

12. **Duplicate tracks (same song in multiple formats).**
    `Track.AudioSha1` is a hash of audio content only (ignoring tag frames). Scanner deduplicates on it.

13. **Two clients request the same uncached transcode simultaneously.**
    Keyed `SemaphoreSlim` in `TranscodeService`. One FFmpeg process per `(AudioSha1, profile)`; others wait. Atomic rename on success; partial tempfile deleted on cancellation.

14. **Trust boundary confusion when the tunnel terminates TLS.**
    `ForwardedHeaders` middleware trusts only the tunnel's internal IP. Otherwise `RemoteIpAddress` is meaningless and rate limiting is exploitable.

15. **MusicKit developer token expires — Apple Music breaks for all users.**
    `MusicKitDeveloperTokenRotationJob` regenerates from the `.p8` weekly. Post-MVP.

16. **Trying to proxy Apple Music audio — DRM kills it.**
    Don't. The backend stores the catalog ID only. The client plays via MusicKit. Post-MVP.

---

## Shared component kit

Reusable UI lives in `ColdHarbourFrontend/src/app/shared/ui/` (e.g. `button/`, `input/`, `form-field/`, `card/`, `modal/`, `badge/`). Rules:

- **Check before you inline.** Before writing UI markup in a page or feature component, check `shared/ui/` for an existing component. If a similar element exists, use it.
- **Extract on second use, not "later."** If an element will plausibly be used in more than one place, build it as a shared component first, then consume it. Do not inline first and "extract later" — that extraction never happens.
- **Standalone, signal-input based.** Components use `input()`, `input.required()`, `model()`. Inputs are typed and have sensible defaults. Components are `OnPush`.
- **Token-driven SCSS.** Components read design tokens from `src/styles.scss` (`var(--accent)`, `var(--border-w)`, `var(--font-headline)`, etc.). Never hardcode colors, border widths, or font families.
- **TDD-covered.** Each shared component has a `*.spec.ts` (Jasmine/Karma) written before the implementation, covering its public API: variant rendering, disabled/loading state, event emission, control-value-accessor read/write.
- **No upward dependencies.** A `shared/ui` component never imports from `features/*` or `core/*`. Verify with `git grep -nR "from '.*features" ColdHarbourFrontend/src/app/shared/ui`.
- **Catalogued.** Each component is listed in `src/app/shared/ui/README.md` and tracked in `docs/FRONTEND_MIGRATION.md`.

## Design patterns

1. **Standalone components** — no NgModule; every component declares its own `imports`.
2. **Zoneless change detection + Signals** — `provideZonelessChangeDetection()`; UI state is signals, not subjects.
3. **RxJS at the HTTP boundary only** — Observables die at `ApiService`; everything downstream is signals.
4. **Web Worker for CPU-bound work** — color extraction in a worker; any pixel/buffer loop >~10ms goes off-thread.
5. **Clean Architecture layering** — strict inward dependency: `Api → Application → Domain`; `Infrastructure → Application → Domain`. `Domain` has zero framework refs.
6. **Rich DDD aggregates** — behavior lives on entities (`PlaybackSession.Transfer(...)`, `RefreshToken.Rotate(...)`), not in anemic services.
7. **Repository + Unit of Work via `DbContext`** — one repository per aggregate root; `SaveChangesAsync` is the transaction boundary.
8. **CQRS-lite via MediatR** — commands return `Unit`; queries return DTOs; validators as a pipeline behavior.
9. **Provider adapter pattern** — `IAudioSource` on the client, `IProviderAdapter` on the server. Adding a new provider means one new adapter.
10. **Server-owned playback session** — `PlaybackSession` lives in-memory on the server; clients are subscribers. `PlayEvent` rows are the durable record.
11. **Refresh token rotation + family revocation** — every refresh mints a new token; reuse triggers family-wide revocation.
12. **Content-addressed cache** — transcode and thumbnail outputs named by hash of inputs; invalidation is implicit.
13. **Idempotent ingest** — `AudioSha1` is the dedup key; running ingest N times produces the same state.
14. **Active-device guard on WS hub** — only the active device's state mutations are accepted; stale events from previously active devices are dropped.
15. **`--accent` as a live CSS variable** — UI accent color updates per-track from album art without a re-render; components read `var(--accent)` and get the change for free.
16. **`loadMusic` idempotency** — loading the same URL twice is a no-op if the audio element is already set up; prevents restarts on component re-mount.

---

## Post-implementation checklist

Before any feature is considered done:

- [ ] **Runs outside Docker:** `dotnet watch run` + `ng serve --proxy-config proxy.conf.json` work.
- [ ] **Runs inside Docker:** `docker compose up --build` succeeds end-to-end.
- [ ] **Layer discipline:** no `Domain` → EF Core/ASP.NET; no `Infrastructure` leaking into `Api` except via DI extension.
- [ ] **DTO ≠ entity:** responses never expose EF entities directly.
- [ ] **Validation at the edge:** every command/query has a `FluentValidation` validator.
- [ ] **Auth coverage:** new endpoints declare `[Authorize]` (or `[AllowAnonymous]` with a comment).
- [ ] **Rate-limited where appropriate:** login, refresh, registration covered.
- [ ] **WebSocket safe:** playback state mutations go through the session hub, not a REST side-channel.
- [ ] **Active-device guard:** WS messages that should only come from the active device are guarded.
- [ ] **Type alignment:** frontend types match DTOs (camelCase, nullability).
- [ ] **Loading + error states:** components reflect `isLoading()` and `error()` signals.
- [ ] **Design system:** new UI follows `DESIGN.md` — 4px borders, no rounded corners, `var(--accent)` for accent colour, correct font assignments.
- [ ] **Keyboard parity:** every new mouse control has a shortcut.
- [ ] **MediaSession coherent:** if playback is affected, `ControllerService` is updated.
- [ ] **No leaks:** `HTMLAudioElement`s, timers, workers, WebSockets, and subscriptions clean up via `DestroyRef`.
- [ ] **No absolute origins:** no hardcoded `http://localhost` or `ws://localhost` in frontend source.
- [ ] **Proxy reviewed:** new routes or WebSockets have matching entries in `proxy.conf.json` and the Caddyfile.
- [ ] **Cookie Secure flag:** any new cookie follows the `!env.IsDevelopment()` pattern.
- [ ] **Content-addressed where applicable:** new caches use input-hash keys.
- [ ] **Backup-safe:** schema changes restore cleanly into an empty Postgres.
- [ ] **Secrets absent:** no new env var value committed; `.env.example` updated.
- [ ] **Doc reconciled:** if behaviour diverged from this file, update here or revert the code.
