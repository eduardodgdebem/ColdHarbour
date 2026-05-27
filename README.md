# ColdHarbour

A self-hosted music player you run on your own machine and expose to the internet over a tunnel. Upload your library, stream it to any browser or phone, and control playback across devices — Spotify-Connect style, where one server owns the session and every client is a remote.

No cloud account, no DRM middleman, no telemetry. Your files stay on your disk.

---

## What it does

- **Upload-driven library.** Drop a track in through the UI; the server hashes it, extracts tags, writes it to a canonical path, and generates artwork thumbnails.
- **Adaptive streaming.** Local files stream byte-for-byte when your device can decode them, or transcode on demand (Opus / AAC / MP3) when it can't. Output is content-addressed and cached, so seeks and replays are instant.
- **Server-owned playback session.** One session per user lives on the server. Any connected device can drive transport (play / pause / next / seek / queue) over WebSocket, and the active device plays the audio. Hand playback off between devices mid-track.
- **Queue, shuffle, repeat.** Stable shuffle order, per-session repeat modes, auto-advance — all arbitrated server-side and persisted across restarts.
- **Internet-exposed by design.** JWT access tokens, rotating refresh tokens with family revocation, short-lived media-token cookies for `<audio>`/`<img>`, and edge hardening assuming the LAN is not trusted.

---

## Architecture

Three processes, one host, behind a tunnel that terminates TLS:

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

- **caddy** serves the built Angular bundle and reverse-proxies `/api/*` and `/ws/*` to the api (WebSocket upgrade included).
- **api** owns domain logic, auth, streaming, transcoding, and the raw WebSocket playback hub. Built with DDD + Clean Architecture (`Api → Application → Domain`, `Infrastructure → Application → Domain`).
- **db** stores the catalog, users, playback history, and refresh tokens. Audio files live on a bind-mounted volume, never in the database.

See [`CLAUDE.md`](./CLAUDE.md) for the full architecture reference and [`docs/DESIGN.md`](./docs/DESIGN.md) for the **Sonic Brutalism** design system.

---

## Tech stack

**Frontend** — Angular 21 (standalone, zoneless, Signals), TypeScript 5.9, RxJS only at the HTTP boundary, SCSS design tokens, Web Worker for album-art color extraction, raw WebSocket client, `MediaSession` + `HTMLAudioElement` (no player libraries).

**Backend** — .NET 10 / ASP.NET Core 10, MediatR (CQRS-lite), EF Core 9 + Npgsql, FluentValidation, JWT bearer auth, Argon2id password hashing, TagLibSharp for metadata, FFmpeg for transcoding, ImageSharp for thumbnails, `IHostedService` for scheduled jobs, Serilog.

**Infra** — Docker Compose (caddy + api + db), Caddy edge proxy, PostgreSQL.

Deliberately **not** in the stack: Redis, HLS, SignalR, lossless-to-lossless transcoding. The reasoning is in `CLAUDE.md`.

---

## Repository layout

```
ColdHarbour/
├── ColdHarbourBackend/        # .NET solution
│   ├── src/
│   │   ├── ColdHarbour.Domain/          # entities, VOs, domain events
│   │   ├── ColdHarbour.Application/     # commands/queries, DTOs, ports
│   │   ├── ColdHarbour.Infrastructure/  # EF Core, repos, auth, jobs
│   │   └── ColdHarbour.Api/             # controllers, WS hub, DI root
│   └── tests/                           # one project per layer
├── ColdHarbourFrontend/       # Angular app
├── caddy/                     # Caddyfile + build Dockerfile
├── content/                   # library / cache / backups (bind mount)
├── docs/                      # DESIGN.md + migration trackers
├── docker-compose.yml
├── Makefile                   # local dev helpers
└── dev.sh                     # tmux-based dev launcher
```

---

## Getting started

### Prerequisites

- Docker + Docker Compose
- .NET 10 SDK (local dev)
- Node 22+ (local dev)
- FFmpeg (bundled in the api image; needed on host only for local transcoding)

### 1. Configure secrets

```bash
cp .env.example .env
# edit .env — set a strong POSTGRES_PASSWORD before exposing anything
```

### 2. Run the whole thing in Docker

```bash
docker compose up --build
```

The UI is served at `http://localhost` (Caddy on port 80).

### 3. Local development (hot reload)

The `Makefile` and `dev.sh` wrap the common loop — start Postgres, run migrations, then launch the api with `dotnet watch` and the frontend with `ng serve` (proxying `/api` and `/ws` to the backend).

```bash
make dev          # Postgres + API + UI, all in one terminal
# or
./dev.sh          # same, but in a tmux session (db | api | ui windows)
```

- API → `http://localhost:8080`
- UI → `http://localhost:4200` (also bound on your LAN IP for phone testing)

Other Make targets:

```bash
make db-start     # start Postgres only
make db-migrate   # apply EF Core migrations
make db-reset     # destroy the DB and re-migrate (destructive)
make build        # build the backend solution
make test         # run all backend tests
make logs         # tail Postgres logs
```

---

## Testing

TDD is mandatory — every piece of production code is preceded by a failing test (`red → green → refactor`), with a 90% minimum coverage bar.

```bash
# Backend — xUnit + FluentAssertions, integration tests hit a real Postgres via Testcontainers
make test
# or: dotnet test ColdHarbourBackend/ColdHarbour.slnx

# Frontend — Jasmine / Karma
cd ColdHarbourFrontend && npm test
```

---

## Configuration

The api is configured entirely through environment variables (Postgres connection, JWT signing key/issuer/audience, token TTLs, content root, cache limits, FFmpeg path, public origin, bootstrap owner credentials). The full table — including the post-MVP Apple Music keys — lives in [`CLAUDE.md`](./CLAUDE.md) under "All environment variables".

The frontend bundle is fully static with no runtime env vars: all URLs are relative (`/api`, `/ws`), and the WebSocket base is derived from `window.location` at load time, so the same build works on localhost, a LAN IP, or a tunnel domain.

---

## Project status

The MVP is complete and running. Progress is tracked in three documents:

- [`docs/MIGRATION.md`](./docs/MIGRATION.md) — backend phases 0–7, **all done**.
- [`docs/FRONTEND_MIGRATION.md`](./docs/FRONTEND_MIGRATION.md) — frontend phases 0–7 (shared kit + pages), **all done**.
- [`docs/PLAYBACK_MIGRATION.md`](./docs/PLAYBACK_MIGRATION.md) — browser-driven → server-authoritative playback, phases 0–5, **all done**.

**Out of scope for v1 (deferred):** Apple Music integration (the provider abstraction is preserved as a seam), HLS, Redis. The MVP ships with a single `local` provider.
