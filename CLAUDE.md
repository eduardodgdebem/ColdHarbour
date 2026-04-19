# ColdHarbour

> Self-hosted music player. The repo today is a minimal seed (Angular SPA + flat ASP.NET + Postgres + two nginx instances, with a mocked `MusicController`). This document describes **the direction of the architecture**, not the frozen state of the code. When code and doc diverge, the code is authoritative — but the divergence must be reconciled (either update this doc or realign the code).

---

## Architecture overview

ColdHarbour is a self-hosted music server that runs entirely in Docker on the user's machine. It will eventually be exposed to the public internet over a tunnel (Cloudflare Tunnel / Tailscale Funnel / similar), so the threat model is **not** "trusted LAN."

```
┌─────────────┐   ┌──────────┐   ┌───────────┐
│    caddy    │──▶│   api    │──▶│ postgres  │
│  (reverse   │   │ (ASP.NET │   │           │
│   proxy +   │   │  .NET 9) │   │           │
│   static)   │   └──────────┘   └───────────┘
└─────────────┘          │
       ▲                 ▼
       │          ┌────────────────────┐
   browser /      │  content library   │
   mobile client  │  (host bind mount) │
                  └────────────────────┘
```

Three processes, one host:
- **caddy** terminates HTTP on port 80, serves the built Angular bundle as static files, and reverse-proxies `/api/*` → api and `/ws/*` → api (WebSocket). Caddy replaces the two nginx instances that exist in the current seed (edge proxy + frontend static server); they collapse into one image because Caddy does both cleanly.
- **api** owns domain logic, auth, provider integration, and streaming for local content.
- **db** stores catalog, users, playback sessions, refresh tokens. Audio files live on a bind-mounted volume, never inside the DB.

Three cross-cutting design axes shape everything below:

1. **Server-owned playback session.** The active device is a property of the server, not the client. Clients publish/subscribe; the server arbitrates.
2. **Provider abstraction.** A `Track` has a provider (`local`, `apple_music`, …). Local tracks stream through the backend; third-party tracks play on the client via the provider's SDK — the backend never proxies DRM'd audio.
3. **DDD + Clean Architecture on the backend.** Dependencies point inward: `Api → Application → Domain` and `Infrastructure → Application → Domain`. The domain has no framework references.

**MVP scope.** The provider abstraction is preserved as an architectural seam, but v1 ships with a single provider (`local`) only. Apple Music integration (MusicKit developer tokens, Music User Tokens, `AppleMusicAudioSource`, related env vars and jobs) is explicitly deferred until after the MVP is running end-to-end.

**Library management is upload-driven, not folder-driven.** The primary path is: user uploads a track through the frontend → api writes it to `library/` at a canonical path → extracts metadata → inserts the DB row. Deletes go through the frontend too. `POST /api/library/sync` is a **secondary** path for reconciling when files land on disk out-of-band (e.g., user drops a bunch of files directly into the mount). The sync button reports a diff (additions/removals/renames) and applies it on confirmation. No filesystem watcher, no weekly full scan — reconciliation is always user-triggered.

---

## Complete tech stack

**Frontend**
- Angular 20 — standalone components, **zoneless** change detection, Signals
- TypeScript 5.9
- RxJS 7.8 — used only at the HTTP boundary
- SCSS
- Web Worker API (`color-worker.ts` for dominant-color extraction)
- `MediaSession` API (OS-level transport controls)
- `HTMLAudioElement` for local playback (no player libs)
- **MusicKit JS** (client-only) for Apple Music playback — loaded lazily when a user links an Apple Music account

**Backend** — target stack
- .NET 9 / ASP.NET Core
- **MediatR** for CQRS-lite (commands return `Unit`, queries return DTOs)
- **EF Core 9 + Npgsql** for persistence
- **FluentValidation** on command/query inputs (as a MediatR pipeline behavior)
- **Microsoft.AspNetCore.Authentication.JwtBearer** for access tokens
- **Isopoh.Cryptography.Argon2** (or equivalent) for password hashing
- **TagLibSharp** for ID3/FLAC/OGG metadata
- **FFmpeg** (shelled out, or via `FFMpegCore`) for transcoding audio
- **SkiaSharp** (or ImageSharp) for artwork thumbnail generation
- **Hangfire** *or* `IHostedService` for scheduled jobs (start with `IHostedService`; move to Hangfire when retries/dashboards become worth the dependency)
- **Serilog** for structured logging
- Swashbuckle OpenAPI — dev-only

**Explicitly *not* in the stack (decisions, not omissions):**
- **Redis** — the caches we need are multi-MB audio/image blobs served with HTTP Range; Redis holds values in RAM and can't do `sendfile`. Filesystem caches win. Session heartbeats stay in an in-memory `IPlaybackSessionStore` (port exists so we can swap later if we ever outgrow a single api container).
- **HLS** — cache-first whole-file transcoding is enough for single-digit users. Revisit if mobile-on-flaky-network becomes a real pain point.
- **Lossless-to-lossless transcoding** — pointless bit repackaging; skipped.

**Infrastructure**
- Docker Compose (services: caddy, api, db)
- **Caddy** (`caddy:alpine`) — edge proxy + SPA static server (both roles in one container)
- PostgreSQL (`postgres:latest` — pin a major version before going public)

**Tooling**
- `dotnet` CLI (local dev: `dotnet watch run`)
- Angular CLI (local dev: `ng serve`)
- Jasmine/Karma on frontend; xUnit + FluentAssertions on backend

---

## All environment variables

Defined in `docker-compose.yml` and `appsettings.*.json`. Variables not yet introduced are marked `[planned]`. Convention: project-specific vars prefixed with `COLDHARBOUR_`.

### api (ASP.NET)
| Variable | Source | Purpose |
|---|---|---|
| `ASPNETCORE_URLS` | compose | Kestrel binding (`http://+:8080`) |
| `ASPNETCORE_ENVIRONMENT` | compose | `Development` / `Production` |
| `ConnectionStrings__DefaultConnection` | appsettings / env override | Postgres connection string |
| `COLDHARBOUR_CONTENT_ROOT` `[planned]` | compose | Library mount point (default `/content`) |
| `COLDHARBOUR_TRANSCODE_CACHE` `[planned]` | compose | Transcode output dir |
| `COLDHARBOUR_TRANSCODE_CACHE_LIMIT_BYTES` `[planned]` | compose | Soft cap for `cache/transcodes/`; LRU eviction above this. Default `5368709120` (5 GB) |
| `COLDHARBOUR_ART_CACHE_LIMIT_BYTES` `[planned]` | compose | Soft cap for `cache/art/`. Default `536870912` (512 MB) |
| `COLDHARBOUR_FFMPEG_PATH` `[planned]` | compose | Path to `ffmpeg` binary (default `/usr/bin/ffmpeg` — installed in the api image) |
| `COLDHARBOUR_SCAN_CRON` `[planned]` | compose | Scanner cron (see pipeline) |
| `COLDHARBOUR_JWT_SIGNING_KEY` `[planned]` | `.env` (secret) | HS256 signing key — ≥256 bits, random |
| `COLDHARBOUR_JWT_ISSUER` `[planned]` | compose | `coldharbour` |
| `COLDHARBOUR_JWT_AUDIENCE` `[planned]` | compose | `coldharbour-web` |
| `COLDHARBOUR_ACCESS_TOKEN_TTL` `[planned]` | compose | Default `15m` |
| `COLDHARBOUR_REFRESH_TOKEN_TTL` `[planned]` | compose | Default `14d` |
| `COLDHARBOUR_PUBLIC_ORIGIN` `[planned]` | compose | e.g. `https://music.example.com` — CORS allowlist + cookie domain |
| `COLDHARBOUR_APPLE_TEAM_ID` `[planned]` | `.env` (secret) | Apple Developer Team ID |
| `COLDHARBOUR_APPLE_KEY_ID` `[planned]` | `.env` (secret) | MusicKit key ID |
| `COLDHARBOUR_APPLE_PRIVATE_KEY_PATH` `[planned]` | `.env` (secret) | Path to `.p8` file mounted into the container |
| `COLDHARBOUR_PROVIDER_CREDENTIALS_KEY` `[planned]` | `.env` (secret) | AES-256 key encrypting per-user provider tokens at rest |

### caddy
| Variable | Purpose |
|---|---|
| `COLDHARBOUR_PUBLIC_HOSTNAME` `[planned]` | Hostname the tunnel forwards to — used by the Caddyfile |

### db (Postgres)
| Variable | Purpose |
|---|---|
| `POSTGRES_USER` | `user` — replace before exposure |
| `POSTGRES_PASSWORD` | **rotate before exposing via tunnel** |
| `POSTGRES_DB` | `coldharbourdb` |

### frontend build
No runtime env vars — the bundle is static. The API base URL must be **relative** in production (`/api`) so the same-origin Caddy routes it; the hardcoded `http://localhost:8080` in `ApiService` today is a dev-only shortcut (see hurdle #1).

### Secrets hygiene
- Secrets never in-repo. `.env` is gitignored; mount the `.p8` as a read-only Docker secret.
- `appsettings.Production.json` may reference env vars via `${VAR}` but never contains the values themselves.

---

## Content directory structure

The media library lives **outside** the image, bind-mounted at `/content` in the api container.

```
/content/
├── library/
│   ├── {artist}/
│   │   └── {album}/
│   │       ├── 01 - {track}.flac
│   │       ├── 01 - {track}.mp3        # optional pre-generated transcode
│   │       ├── cover.jpg               # preferred UI artwork
│   │       └── album.json              # metadata override (optional)
│   └── ...
├── playlists/
│   └── {id}.m3u8                       # imported playlists
├── cache/
│   ├── transcodes/
│   │   └── {cacheKey}.{ext}            # cacheKey = sha256(AudioSha1 || profile)
│   └── art/
│       ├── {artSha1}-source.{jpg|png|webp}   # original extracted art
│       ├── {artSha1}-64.webp                 # thumb (list rows)
│       ├── {artSha1}-256.webp                # tile (grid views)
│       └── {artSha1}-1024.webp               # full (player hero)
└── backups/
    └── coldharbour-{YYYYMMDD}.sql.gz   # nightly Postgres dumps
```

**Current state:** sample assets live in `ColdHarbourBackend/wwwroot/assets/`. That is a temporary placeholder and must move to the layout above when the scanner is implemented.

Rules:
- The scanner never **writes** into `library/` — it only reads. All writes go to `cache/`.
- `album.json`, when present, overrides ID3 tags.
- Directory names are a fallback source of truth when tags are missing or mojibake'd.

---

## Backend layout (DDD + Clean Architecture)

Current state is a single flat project (`ColdHarbourBackend/` with `Program.cs`, `Controllers/`). Target layout splits it into layered projects inside a solution:

```
ColdHarbourBackend/
├── ColdHarbour.sln
├── src/
│   ├── ColdHarbour.Domain/          # entities, VOs, domain events, domain services, interfaces
│   ├── ColdHarbour.Application/     # use cases (commands/queries), DTOs, validators, ports
│   ├── ColdHarbour.Infrastructure/  # EF Core, repositories, provider adapters, auth infra
│   └── ColdHarbour.Api/             # controllers, middleware, SignalR/WS hub, composition root
└── tests/
    ├── ColdHarbour.Domain.Tests/
    ├── ColdHarbour.Application.Tests/
    └── ColdHarbour.Api.IntegrationTests/
```

**Dependency rule:** `Domain` references nothing. `Application` references `Domain`. `Infrastructure` references `Application` + `Domain`. `Api` references `Application` (+ `Infrastructure` only for DI registration, via a `DependencyInjection.cs` extension method per layer).

**Bounded contexts** inside `Domain`:
- **Library** — `Track`, `Album`, `Artist`; aggregate boundary on `Album`.
- **Playback** — `PlaybackSession` (aggregate root per user), `Device` (entity), `PlayEvent`.
- **Identity** — `User` (aggregate root), `RefreshToken` (entity), value objects `PasswordHash`, `Role`.
- **Integration** — `ProviderLink` (per-user, per-provider credentials), adapter ports for each provider.

Entities are **rich** (behavior lives with data): e.g. `PlaybackSession.TransferTo(deviceId)` enforces that the requesting user owns the device and emits a `PlaybackTransferred` domain event.

---

## Streaming and storage

### Audio streaming

**Endpoint:** `GET /api/stream/{trackId}` — authorized. Resolves `Track.Id → Track.LocalPath` from the DB. The URL never takes a filesystem path (eliminates path-traversal).

**Two serving modes, decided per request:**

- **Pass-through** when the requesting device can natively decode the track's codec, and no bitrate cap is in effect. `PhysicalFile(path, mime, enableRangeProcessing: true)` — ASP.NET handles `Range`, `206`, `ETag`, `RequestAborted` cancellation. Kernel `sendfile`; zero CPU.
- **Transcoded** otherwise. FFmpeg converts the source → a cache file → served with Range support on subsequent reads. **First play warms the cache (~1–3s); every seek and replay is instant.**

**Transcode profiles** (finite, named set):

| Profile | Codec / bitrate | Use case |
|---|---|---|
| `original` | source, no transcode | Client supports native codec |
| `opus-128` | Opus 128 kbps | Network-efficient default |
| `aac-192` | AAC 192 kbps | Safari-safe fallback |
| `mp3-192` | MP3 192 kbps | Legacy playback |

Lossless-to-lossless transcoding is intentionally absent. Clients advertise codec capabilities + preferred profile when they register as a `Device`; the server picks the cheapest profile that works.

**Content-addressed cache.** Cache key = `sha256(AudioSha1 || profile)`. File lives at `/content/cache/transcodes/{cacheKey}.{ext}`. Reusable across users — same FLAC + same profile = same cache hit. `ETag: "{cacheKey}"`, `Cache-Control: max-age=31536000, immutable`.

**Concurrency.** If two clients request the same uncached transcode at once, only one FFmpeg process runs; the second waits on the same tempfile→rename completion. Implement with a keyed `SemaphoreSlim` map. Temp filename is `{cacheKey}.{ext}.tmp-{uuid}`; atomic rename on success.

**Cancellation.** `HttpContext.RequestAborted` tokens the file read. For in-progress transcodes, killing the response kills the FFmpeg process via the token; the partial tempfile is deleted.

**Not in v1:** HLS, DASH, gapless playback across tracks.

### Supported formats (first pass)

| Format | Tag extraction | Browser decode | Serving mode |
|---|---|---|---|
| MP3 | ✅ | universal | pass-through |
| FLAC | ✅ | Chrome/FF/Edge, Safari 14+ | pass-through; transcode for older Safari |
| M4A / AAC | ✅ | universal | pass-through |
| ALAC (.m4a) | ✅ | Safari only | transcode for non-Safari |
| Ogg Vorbis | ✅ | Chrome/FF, **not** Safari | transcode for Safari |
| Opus | ✅ | Chrome/FF, Safari 14+ | pass-through |
| WAV | ✅ | universal | pass-through |
| WMA, APE, DSD, CUE+FLAC | — | — | **out of scope v1** |

### Artwork

**Source priority** (decided at scan time, recorded on `Album.CoverArtSha1`):
1. **Embedded image** (ID3 `APIC`, FLAC picture block, Vorbis `METADATA_BLOCK_PICTURE`, MP4 `covr`). Extracted once, dumped to `cache/art/{artSha1}-source.{ext}`.
2. **Sidecar file** next to the audio: `cover.jpg`, `folder.jpg`, `album.jpg` (first match, case-insensitive).
3. **Generated placeholder** — deterministic gradient seeded by the album name hash; also cached.

**Endpoint:** `GET /api/artwork/{albumId}?size=64|256|1024`. Generates the requested size on first request (SkiaSharp, WebP q=85), caches to `cache/art/{artSha1}-{size}.webp`, serves. ETag = `{artSha1}-{size}`. Same `max-age=immutable` rules.

### Storage rules

- **DB holds metadata only.** Never blobs. Rows hold paths relative to `COLDHARBOUR_CONTENT_ROOT`, so the root is relocatable.
- **`library/` is read-only to the app.** All app writes go to `cache/`.
- **Cached files are derived and content-addressed** — safe to delete any subset; LRU prune is safe.
- **No file cache in Redis, ever.** See tech stack for the reasoning.

---

## Services, jobs, and models per app

### api (ColdHarbour.*)

**Domain aggregates / entities**
- `Track { Id, Title, Artist, Album, Duration, Provider, ProviderRef, LocalPath?, Format, Bitrate, AudioSha1 }`
- `Album { Id, Title, Artist, Year, CoverPath, Tracks[] }` (aggregate root for tracks)
- `Artist { Id, Name }`
- `Playlist { Id, OwnerId, Name, Description, CoverPath, Tracks[] }`
- `User { Id, Name, Email, PasswordHash, Role, TotpSecret?, CreatedAt }` (role: `owner` | `user`)
- `RefreshToken { Id, UserId, DeviceId, TokenHash, ExpiresAt, RevokedAt?, ReplacedById?, CreatedByIp, UserAgent, FamilyId }`
- `Device { Id, UserId, Name, UserAgent, Capabilities (providers it can play), LastSeenAt }`
- `PlaybackSession { UserId (PK), ActiveDeviceId, TrackId, Provider, ProviderRef, PositionMs, IsPlaying, UpdatedAt }` — one per user
- `PlayEvent { Id, UserId, TrackId, Provider, StartedAt, EndedAt, CompletedRatio }`
- `ProviderLink { UserId, Provider, EncryptedCredentials, LinkedAt }`

**Application use cases** (CQRS via MediatR)
- Commands: `RegisterUser`, `AuthenticateUser`, `RefreshAccessToken`, `RevokeRefreshTokenFamily`, `RegisterDevice`, `TransferPlayback`, `UpdatePlaybackPosition`, `UploadTrack`, `DeleteTrack`, `SyncLibrary`, `ImportPlaylist`
- Queries: `GetPlaylist`, `SearchTracks`, `GetActiveSession`, `ListDevices`, `GetTopArtists`, `PreviewLibrarySync` (dry-run: returns the diff without applying it)

**Domain/Infrastructure services**
- `TrackIngestService` (Infrastructure, implements `ITrackIngestService` from Application) — handles upload path: validates file, computes `AudioSha1`, extracts tags via TagLibSharp, writes to canonical `library/` path, persists `Track`/`Album`/`Artist` aggregates. Also the sole caller on delete. Never mutates existing tracks' audio content.
- `LibraryReconciler` (Infrastructure, implements `ILibraryReconciler` from Application) — backs the sync button. Walks `library/`, compares to DB, emits a diff (new / missing / changed-in-place via `AudioSha1`). User confirms → reconciler applies. Holds an advisory Postgres lock while running; never touches files under an active play session.
- `TranscodeService` — on-demand audio conversion, content-addressed output.
- `ArtworkService` — extract/store thumbnails (client-side worker handles color extraction).
- `AppleMusicAdapter` — issues MusicKit developer tokens, refreshes Music User Tokens. **Does not proxy audio.**
- `LocalStreamingService` — serves bytes with Range support from `library/`.
- `TokenService` — signs JWTs, mints+rotates refresh tokens, handles theft detection.
- `PasswordHasher` — Argon2id wrapper.
- `PlaybackSessionHub` — SignalR (or raw WebSocket) hub; brokers device events.
- `IPlaybackSessionStore` (port in `Application`, impl in `Infrastructure`) — backs `PlaybackSession` state + position heartbeats. Ship with `InMemoryPlaybackSessionStore` (`ConcurrentDictionary<UserId, PlaybackSession>`). Material playback events (play/pause/complete) persist to Postgres through repositories; heartbeat positions stay in-memory. Swap to a Redis impl later only if multi-instance api or restart-durable sessions become real requirements.

**Scheduled jobs** (`Jobs/`, `IHostedService` at first)
- `BackupJob` — `pg_dump` to `/content/backups/`.
- `CachePruneJob` — LRU eviction on `cache/transcodes`.
- `ArtCachePruneJob` — LRU eviction on `cache/art`.
- `PlaybackStatsJob` — rebuild weekly aggregates.
- `IntegrityCheckJob` — sample-verify `AudioSha1` of 5% of tracks; flag drift in the DB but never auto-delete.
- `RefreshTokenSweepJob` — delete expired/revoked refresh tokens nightly.

Library sync is **user-triggered**, not scheduled. No filesystem watcher, no weekly full walk.
`MusicKitDeveloperTokenRotationJob` is post-MVP (returns when the Apple Music adapter ships).

### frontend (`ColdHarbourFrontend/src/app/`)

**Services** (`services/`, `providedIn: 'root'`)
- `ApiService` — single HTTP boundary; attaches bearer token; handles `401 → refresh → retry once`.
- `AuthService` — login/logout, holds access token in memory, schedules silent refresh.
- `MusicService` — domain state signals (current music, playlist, navigation).
- `AudioSource` interface with two implementations:
  - `LocalAudioSource` — wraps `HTMLAudioElement`.
  - `AppleMusicAudioSource` — wraps MusicKit JS instance.
  Both expose the same signals (`isPlaying`, `position`, `duration`, `volume`). Selected based on the current track's provider.
- `PlaybackSessionService` — WebSocket client; publishes heartbeats when this device is active, subscribes to server events when it isn't.
- `ColorService` — dominant-color extraction via Web Worker, exposed as CSS var `--accent`.
- `ControllerService` — keyboard shortcuts + `MediaSession` integration.

**Components / pages / icons / workers** — as they exist today, plus: `LoginPageComponent`, `DevicesPageComponent` (list + "play here"), `AccountPageComponent` (link Apple Music).

---

## Authentication (internet-exposed)

Because the server is reachable over a tunnel, the auth story is non-trivial.

**Tokens**
- **Access token**: JWT, HS256, ~15min TTL, claims `sub`, `role`, `jti`, `deviceId`. Held **in memory** by the SPA; sent as `Authorization: Bearer`. Never in localStorage.
- **Refresh token**: opaque random 256-bit, delivered as `HttpOnly` + `Secure` + `SameSite=Strict` cookie scoped to `/api/auth`. ~14 day TTL. **Rotated on every use** (single-use); reuse of an already-consumed token revokes the entire token `FamilyId` (theft detection).

**Storage**
- `RefreshToken` holds only a SHA-256 hash of the token value. Plaintext never persisted.
- One family per login; "logout everywhere" = revoke the family.

**Password + defenses**
- Argon2id (current OWASP recommendation).
- ASP.NET `RateLimiter` on `/api/auth/login` (e.g. 5/min/IP, 10/hr/user) and `/api/auth/refresh`.
- Account lockout: 10 failed attempts → 15 min cooldown.
- Optional TOTP 2FA for the `owner` role.

**WebSocket auth**
- Handshake carries the access token. On `exp`, server closes with code `4001`; client refreshes and reconnects. Simpler than in-band refresh messaging.

**Edge hardening (Caddy + tunnel)**
- Tunnel terminates TLS. Do **not** also do TLS in Caddy — `auto_https off` in the Caddyfile.
- Configure `ForwardedHeaders` middleware in ASP.NET with the tunnel's internal IP as the sole trusted proxy.
- Security headers via Caddy: `Strict-Transport-Security`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, and a CSP that allows MusicKit origins (`js-cdn.music.apple.com`, `play.music.apple.com`).

---

## 12 common hurdles with documented solutions

1. **`ApiService` hardcodes `http://localhost:8080` — breaks in production.**
   *Fix:* use relative `/api` in prod so Caddy handles it. Keep the absolute URL only in `environment.development.ts`.

2. **`AllowAll` CORS policy leaks to production.**
   *Fix:* in `Production`, restrict origins to `COLDHARBOUR_PUBLIC_ORIGIN`; keep `AllowAll` in `Development` only.

3. **Seek on large files stalls because the endpoint doesn't support Range.**
   *Fix:* `PhysicalFile(path, mime, enableRangeProcessing: true)`. Return `206 Partial Content`.

4. **Two concurrent requests race on the same refresh token.**
   *Fix:* refresh rotation is single-use — wrap "mark used + issue new" in a serializable transaction. Reuse of an already-consumed token revokes the entire `FamilyId` and forces re-login.

5. **Access token expires mid-WebSocket; the socket silently serves a stale identity.**
   *Fix:* server closes with code `4001` when `exp` is reached; client catches, calls `/auth/refresh`, reconnects.

6. **MusicKit developer token expires (Apple caps at 6 months) and Apple Music playback breaks for all users at once.**
   *Fix:* `MusicKitDeveloperTokenRotationJob` regenerates from the `.p8` key weekly. Clients fetch on init and on `401` from MusicKit.

7. **Trying to proxy Apple Music audio to unify streaming — DRM kills it.**
   *Fix:* don't. The backend only stores the catalog ID. The active client plays via MusicKit. Linux/Android clients can't be the active playback device for Apple Music tracks — platform constraint, not a bug.

8. **Fast track switching leaks `HTMLAudioElement` instances.**
   *Fix:* `LocalAudioSource.cleanupAudio()` must `pause()`, clear `src`, and drop the element on every switch. Already handled in today's `AudioService`; preserve the pattern when splitting into sources.

9. **Duplicate tracks (same song in multiple formats) create duplicate rows.**
   *Fix:* `Track.AudioSha1` is a hash of *audio content* (ignoring tag frames). The scanner deduplicates on it.

10. **Trust boundary confusion when the tunnel terminates TLS.**
    *Fix:* configure `ForwardedHeaders` middleware with the tunnel's internal IP as the only trusted proxy; otherwise `RemoteIpAddress` is meaningless and rate limiting is exploitable.

11. **Weak default DB credentials from the compose file leak into production.**
    *Fix:* before the first tunnel bring-up, move creds to gitignored `.env`, rotate the password, and remove the `5432:5432` host port mapping so Postgres isn't reachable from the tunnel surface.

12. **Two clients request the same uncached transcode simultaneously; FFmpeg runs twice and writes collide.**
    *Fix:* keyed `SemaphoreSlim` (by cache key) in `TranscodeService`. Only one FFmpeg process per `(AudioSha1, profile)`; others wait on the shared completion. Write to `{cacheKey}.{ext}.tmp-{uuid}`, atomic rename on success, delete on cancellation. Disk fill is a separate concern handled by `CachePruneJob` + `COLDHARBOUR_TRANSCODE_CACHE_LIMIT_BYTES`.

---

## 14 design patterns of the project

1. **Standalone components** — no NgModule; every component declares its own `imports`.
2. **Zoneless change detection + Signals** — `provideZonelessChangeDetection()`; UI state is signals, not subjects.
3. **RxJS at the HTTP boundary only** — Observables die at `ApiService`; everything downstream is signals.
4. **Web Worker for CPU-bound work** — color extraction in a worker; any pixel/buffer loop >~10ms goes off-thread.
5. **Clean Architecture layering** — strict inward dependency direction: `Api → Application → Domain`; `Infrastructure → Application → Domain`. `Domain` has zero framework refs.
6. **Rich DDD aggregates** — behavior lives on entities (`PlaybackSession.TransferTo(...)`, `RefreshToken.Rotate(...)`), not in anemic services.
7. **Repository + Unit of Work via `DbContext`** — one repository per aggregate root; `SaveChangesAsync` is the transaction boundary.
8. **CQRS-lite via MediatR** — commands mutate and return `Unit`; queries return DTOs; validators run as a pipeline behavior.
9. **Provider adapter pattern** — `IAudioSource` on the client, `IProviderAdapter` on the server. Adding Spotify means one new adapter, not a change to the core.
10. **Server-owned playback session** — canonical `PlaybackSession` lives in the DB; clients are views with optional playback capability.
11. **Refresh token rotation + family revocation** — every refresh mints a new token and invalidates the previous; reuse triggers family-wide revocation.
12. **Content-addressed cache** — transcode and thumbnail outputs named by hash of inputs; invalidation is implicit.
13. **Idempotent scanner** — running the scanner N times produces the same state (`AudioSha1` is the key).
14. **Graceful degradation + keyboard parity** — MediaSession, MusicKit, and all mouse actions have working fallbacks; every mouse control has a keyboard shortcut.

---

## Complete weekly pipeline with schedules

All times in `America/Sao_Paulo` (`TZ` on the api container). Scheduler is `IHostedService` at first.

| Day | Time | Job | What it does |
|---|---|---|---|
| Thursday | 03:00 | `CachePruneJob` | LRU eviction on `cache/transcodes` |
| Thursday | 03:15 | `ArtCachePruneJob` | LRU eviction on `cache/art` |
| Friday | 03:00 | `PlaybackStatsJob` | Recompute weekly aggregates (top artists/albums) |
| Saturday | 03:00 | `BackupJob` | `pg_dump` → `/content/backups/`, retain 4 latest |
| Sunday | 03:00 | `IntegrityCheckJob` | Sample-verify `AudioSha1` on 5% of tracks; DB flag only |
| Daily | 04:00 | `RefreshTokenSweepJob` | Delete expired/revoked refresh tokens |

User-triggered, event-driven:
- `SyncLibrary` — runs on the sync button; walks `library/`, emits diff, applies on confirmation.
- `TranscodeOnDemand` — fired when a client requests a format/bitrate not in the cache.

---

## Post-implementation checklist

Before any feature is considered done:

- [ ] **Runs outside Docker:** `dotnet watch run` + `ng serve` still work (fast dev loop).
- [ ] **Runs inside Docker:** `docker compose up --build` succeeds and the end-to-end flow still works.
- [ ] **Layer discipline:** no `Domain` reference to EF Core/ASP.NET; no `Infrastructure` leaking into `Api` except via DI registration.
- [ ] **DTO ≠ entity:** responses never expose EF entities directly.
- [ ] **Validation at the edge:** every command/query has a `FluentValidation` validator registered.
- [ ] **Auth coverage:** new endpoints declare `[Authorize]` (or `[AllowAnonymous]` with a comment saying why).
- [ ] **Rate-limited where appropriate:** login, refresh, registration covered by the limiter.
- [ ] **WebSocket safe:** if the feature mutates playback state, it goes through the session hub, not a REST side-channel.
- [ ] **Type alignment:** frontend types match DTOs (camelCase, nullability).
- [ ] **Loading + error states:** components reflect `isLoading()` and `error()` signals.
- [ ] **Keyboard parity:** every new mouse control has a shortcut.
- [ ] **MediaSession coherent:** if playback is affected, `ControllerService` is updated.
- [ ] **No leaks:** new `HTMLAudioElement`s, timers, workers, and subscriptions clean up via `DestroyRef` / `ngOnDestroy`.
- [ ] **Idempotency:** scheduled jobs can run twice without corruption.
- [ ] **No absolute origins:** no new hardcoded `http://localhost` in frontend source.
- [ ] **Proxy reviewed:** new routes or WebSockets have the right Caddyfile entry.
- [ ] **Content-addressed where applicable:** new caches use input-hash keys.
- [ ] **Backup-safe:** schema changes restore cleanly into an empty Postgres.
- [ ] **Secrets absent:** no new env var with a value committed; `.env.example` updated.
- [ ] **Doc reconciled:** if behavior diverged from this file, either update here or revert the code.
