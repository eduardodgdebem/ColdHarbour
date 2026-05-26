# Playback Authority Migration — Browser-driven → Server-authoritative (Spotify-Connect-style)

> Path from today's playback model (server owns `activeDeviceId`/`trackId`/`positionMs`/`isPlaying`; the browser owns the queue, prev/next, seek, auto-advance, shuffle, repeat) to a Spotify-Connect-style model where the **server arbitrates a durable queue and any device can control any other**.
>
> Five phases. Each phase ends with a **working, deployable system** — no phase breaks the app. The WebSocket protocol grows incrementally, so phases roll out sequentially and must remain compatible with whatever is on `main` at every commit.
>
> **TDD is mandatory** (see `CLAUDE.md` § "Working agreement"). Every domain method, command handler, hub branch, and service method is preceded by a failing xUnit / Jasmine spec. No "tests after." The 90 % minimum coverage holds throughout.
>
> **Branch + merge workflow.** Each phase happens on its own `playback-phase-N-<slug>` branch and lands as one PR. Branches are cut **from `main`** after the prior phase merges — never stacked on an in-flight branch. A phase only merges to `main` after its *Definition of done* passes (tests, two-device manual smoke, the `CLAUDE.md` post-implementation checklist). The post-implementation checklist in `CLAUDE.md` applies to every phase.
>
> **This file is the progress tracker.** When a phase completes, flip its row in the Status table to `✅ Done — landed on <branch> (<commit>) <YYYY-MM-DD>` in the same PR that lands the work. If the phase changed any architectural fact, update `CLAUDE.md` in the same PR.

## Status

| Phase | Title | Status |
|---|---|---|
| 0 | Baseline | ✅ Done — landed on `playback-phase-0-baseline` 2026-05-23 |
| 1 | Server-side queue (silent rollout) | ✅ Done — landed on `playback-phase-1-server-queue` 2026-05-23 |
| 2 | Server-driven transport (any device can command) | ✅ Done — landed on `playback-phase-2-server-transport` 2026-05-23 |
| 3 | Auto-advance + Shuffle + RepeatMode | ✅ Done — landed on `playback-phase-3-shuffle-repeat` 2026-05-24 |
| 4 | Queue mutations from any device | ✅ Done — landed on `playback-phase-4-queue-mutations` 2026-05-26 |
| 5 | Restart-durable session (Postgres) | Pending |

---

## Phase 0 — Baseline (current state) ✅ Done

What exists today (the gap this migration closes):

**Server-authoritative already:**

- `PlaybackSession` aggregate (in-memory, per user) holds `ActiveDeviceId`, `TrackId`, `PositionMs`, `IsPlaying`, `UpdatedAt`.
- WS hub at `/ws/playback` accepts `start | pause | resume | heartbeat | transfer | stop`. Active-device guard enforces that only the active device's `pause/resume/heartbeat/stop` are accepted.
- `Device` entity is persisted with codec capabilities + `LastSeenAt`. `InMemoryConnectedDeviceStore` tracks which devices have an open WS right now.
- `PlayEvent.Begin()` is persisted when playback starts.

**Still browser-authoritative (the gap):**

- **The queue** — `MusicService.currentPlayList.musics[] + currentMusicIndex` lives entirely in the browser; the server has no idea what plays next.
- **Prev/next** — pure local index math in `MusicService.nextMusic / previousMusic`; server is told about the new track via a new `start` after the fact.
- **Seek** — never sent to the server; only the next heartbeat (up to 2 s later) carries the new position.
- **Auto-advance** — local `<audio>` `ended` event triggers `nextMusic()` locally.
- **Volume, shuffle, repeat** — don't exist server-side at all.
- **Session durability** — `InMemoryPlaybackSessionStore` loses state on api restart. `PlayEvent.Complete()` exists in the domain but is never called.

**Baseline verification (this phase's only deliverable beyond the doc itself):**

- `cd ColdHarbourBackend && dotnet test ColdHarbour.sln` — green.
- `cd ColdHarbourFrontend && npm test -- --watch=false` — green.
- `docker compose up --build` — three services (`caddy`, `api`, `db`) come up; login + playback work end-to-end via Caddy on `http://localhost`.

No code change beyond this doc and a one-line cross-reference added to `docs/MIGRATION.md`, `docs/FRONTEND_MIGRATION.md`, and `CLAUDE.md`. This phase exists to lock the regression baseline before the protocol starts changing.

---

## Phase 1 — Server-side queue (silent rollout)

> Branch: `playback-phase-1-server-queue`.

**Goal.** Server learns the queue. **No user-visible behavior change yet** — local navigation still drives playback. A second device merely *sees* the queue on connect.

**Changes.**

- **Domain (`ColdHarbour.Domain/Playback/PlaybackSession.cs`):** add `Queue: IReadOnlyList<Guid>` and `QueueIndex: int`. New methods `SetQueue(trackIds, startIndex)` and `MoveTo(index)`. Invariant: `Queue.Count > 0 ⇒ 0 ≤ QueueIndex < Queue.Count`.
- **Hub message (incoming):** `setQueue { trackIds: Guid[], startIndex: int, deviceId }`. Sent by the frontend when a playlist loads or a track is picked.
- **Application:** new `SetQueueCommand` + handler. `PlaybackSessionDto` gains `queue: string[]` and `queueIndex: number`.
- **Frontend:** `MusicService.selectMusic` and `setCurrentPlaylist` push a `setQueue` to the hub. Local `currentPlayList` / `currentMusicIndex` remain the source of truth for navigation in this phase only.
- **Deprecated alias:** `start` keeps working but its handler internally rewrites to `setQueue({ trackIds: [trackId], startIndex: 0 })` so we can retire it cleanly in Phase 2.

**Definition of done.**

- All existing playback tests still green; new specs cover `SetQueue`/`MoveTo` (domain), `SetQueueCommandHandler` (application), the hub branch (integration), and the `MusicService → setQueue` push (frontend).
- Manual two-device smoke: log in on Profile A and B. Profile A picks a playlist. Profile B's `session` message now contains the full queue + current index. UI on B has no visible change yet; this is observable in DevTools.
- `cd ColdHarbourBackend && dotnet test` ≥ 90 % coverage on `Playback`; `cd ColdHarbourFrontend && npm test --watch=false` ≥ 90 %.

---

## Phase 2 — Server-driven transport (any device can command)

> Branch: `playback-phase-2-server-transport`.

**Goal.** Prev/next/seek/pause/resume become hub commands accepted from **any device**. Local mutations are removed. This is the headline phase — Spotify Connect's core promise.

**Changes.**

- **Hub messages (incoming):** add `next { deviceId }`, `previous { deviceId }`, `seek { positionMs, deviceId }`. Widen `pause` and `resume` to accept commands from any device (drop the active-device guard on these two; keep it strictly for `heartbeat`).
- **Sender-becomes-active rule:** if the session has no `ActiveDeviceId`, any incoming transport command sets it to the sender. Otherwise the command targets the existing active device (pause/resume/next/previous/seek affect the *active* device, not the sender — only `transfer` moves activity, same as today).
- **Application:** new `NextTrackCommand`, `PreviousTrackCommand`, `SeekCommand`. Each broadcasts the updated `session`. Server computes the new `TrackId` from `Queue`/`QueueIndex`.
- **Single update channel:** the `session` broadcast remains the only outbound update channel; clients pattern-match on field changes to react.
- **Frontend (`features/player/services/playback-session.service.ts`):** add `next()`, `previous()`, `seek(ms)`, `pause()`, `resume()` — they **only send WS messages**, never call `AudioService` directly. `MusicService.nextMusic` / `previousMusic` become deprecated forwarders to the hub, removed at the end of the phase.
- **Player + ControllerService:** every transport button (mouse, keyboard, MediaSession) routes through `PlaybackSessionService`.
- **Active-device player effect:** when the broadcast `session.trackId` changes, the active device loads + plays the new track and seeks to `positionMs`. **Drift tolerance:** if incoming `positionMs` is within 1 s of `audio.currentTime`, do not seek — prevents audible glitches on every heartbeat-echo.
- **Retire `start`:** delete the deprecated alias from the hub and from `PlaybackSessionService`.
- **Retire echo-suppression flags:** `suppressStart` / `suppressPauseResume` exist today to silence echoes from local mutations. With server-authoritative transport, local mutations are gone — the flags should be **removed** in this phase, not grown.

**Definition of done.**

- From Profile B, "next" while A is playing → A advances within ~250 ms over LAN. Same for previous, seek, pause, resume.
- Drift correction: `<audio>` does not visibly stutter when the server's heartbeat-echo arrives. Verified by listening for 5 minutes uninterrupted.
- `git grep -n "suppressStart\\|suppressPauseResume\\|nextMusic\\|previousMusic" ColdHarbourFrontend/src` returns no live references.
- All test suites ≥ 90 %. New specs cover each command handler, each hub branch, and the player's drift-tolerance logic.

---

## Phase 3 — Auto-advance + Shuffle + RepeatMode

> Branch: `playback-phase-3-shuffle-repeat`.

**Goal.** Tracks chain automatically. Users can shuffle and loop. `PlayEvent.Complete` finally fires.

**Changes.**

- **Domain:** `PlaybackSession` gains `RepeatMode: enum { Off, All, One }` and `Shuffle: bool`. New `AdvanceAfterEnd()` picks the next track honoring both flags. When `Shuffle` is on, server keeps an internal **stable shuffled order** seeded once per queue-mutation (no immediate repeats; reseeds on `setQueue` / `addToQueue` / `setShuffle(true)`).
- **Hub messages (incoming):** `setRepeatMode { mode }`, `setShuffle { enabled }`, `trackEnded { trackId, deviceId }`.
- **Frontend:** `LocalAudioSource.ended` → `PlaybackSessionService.trackEnded(trackId)`. Shuffle + repeat toggles on the `/player` page using the existing `shared/ui` kit (no new primitives).
- **PlayEvent closure:** wire `PlayEvent.Complete(durationMs, positionMs)` into the `trackEnded` handler — closes the existing data gap in `ColdHarbour.Application/Playback`.

**Definition of done.**

- Track plays to its end → next queue item starts automatically on the active device, broadcast to all subscribers.
- Repeat-one → same track restarts on `trackEnded`. Repeat-all → wraps queue. Off → stops after the last track (`IsPlaying = false`, `TrackId = null`).
- Shuffle on → next picks are non-sequential and stable: no track repeats within a queue cycle. Covered by a deterministic-seed spec.
- New specs cover the `AdvanceAfterEnd` matrix (off × all × one × shuffle on/off). `PlayEvent.Complete` is invoked exactly once per ended track (DB assertion).

---

## Phase 4 — Queue mutations from any device

> Branch: `playback-phase-4-queue-mutations`.

**Goal.** Any device can add, remove, reorder, or clear; every device sees it.

**Changes.**

- **Hub messages (incoming):** `addToQueue { trackId, position?, deviceId }`, `removeFromQueue { index, deviceId }`, `reorderQueue { from, to, deviceId }`, `clearQueue { deviceId }`.
- **Domain:** matching methods on `PlaybackSession`. Preserve `QueueIndex` invariant across edits:
  - removing the item at `QueueIndex` → advance to the next (or wrap per `RepeatMode`); if queue empties, clear `TrackId` and set `IsPlaying = false`.
  - removing an item *before* `QueueIndex` → decrement `QueueIndex`.
  - reordering across `QueueIndex` → update `QueueIndex` to follow the originally-current item.
- **Frontend:** make the queue panel on `/player` interactive (drag-to-reorder, swipe/X to remove, "Clear queue" button). Add an **"Add to queue"** action on track rows in `/library` and `/playlist/:id` using the existing kit (`Button`, possibly a small overflow menu primitive only if reuse can't cover it).

**Definition of done.**

- From Profile B, "Add to queue" track X → Profile A's `/player` shows X in queue immediately.
- Reorder on A → B reflects within ~250 ms.
- Remove the currently-playing track on B → A advances to the next without missing a beat. Spec covers the "remove-current-item" branch explicitly.
- All four queue-mutation message types are covered by domain + hub + frontend specs.

---

## Phase 5 — Restart-durable session (Postgres)

> Branch: `playback-phase-5-durable-session`.

**Goal.** `docker compose restart api` no longer loses the queue/position. Closes the explicit deferral from `docs/MIGRATION.md` Phase 6 ("current session position is lost (acceptable for MVP)").

**Changes.**

- **Migration `0004_PlaybackSessionSnapshot`:** one row per `UserId`. Columns: `UserId`, `ActiveDeviceId?`, `TrackId?`, `PositionMs`, `IsPlaying`, `Queue jsonb`, `QueueIndex`, `RepeatMode`, `Shuffle`, `UpdatedAt`.
- **Infrastructure:** new `PostgresPlaybackSessionStore : IPlaybackSessionStore`. **Write-through** on every material mutation (`setQueue`, `next`, `previous`, `seek`, `pause`, `resume`, `setRepeatMode`, `setShuffle`, all queue mutations). **Throttle** heartbeat writes to one snapshot per ~5 s (heartbeats fire every 2 s, naive write = ~30/min/user).
- **Hydrate** on first `GetOrCreate(userId)` after process start. If no row exists, return a fresh session.
- **Wire-up:** swap `InMemoryPlaybackSessionStore` for the Postgres impl in `DependencyInjection.cs`. Keep the in-memory impl available for the test suite; integration tests cover the Postgres path via Testcontainers (the project already uses Testcontainers for Postgres — see `ColdHarbour.Api.IntegrationTests`).

**Definition of done.**

- `docker compose restart api` while a track is playing on Profile A. Both A and B reconnect within seconds, see the same queue + index + position (±5 s for the last heartbeat). User presses play → resumes from the snapshotted position.
- New specs in `Infrastructure.Tests/Playback/PostgresPlaybackSessionStoreTests.cs` cover round-trip + throttling behavior.
- `Application.Tests` for command handlers stay unchanged (they consume the port, not the impl).

---

## Per-phase checklist (use on every PR)

See `CLAUDE.md` § "Post-implementation checklist" — apply to the feature landed in each phase. The bar is: the phase's *Definition of done* is met, and the checklist passes against the code delivered. Notable items for this migration specifically:

- **Active-device guard:** as of Phase 2, transport commands (`pause`/`resume`/`next`/`previous`/`seek`) intentionally **drop** the active-device guard. `heartbeat` keeps it. Call this out in every PR description so the diff doesn't look like a regression.
- **No absolute origins / no hardcoded localhost:** all new WS message types ride the existing `/ws/playback` socket; no new endpoints, no new proxy.conf or Caddyfile entries.
- **Cookie Secure flag, layer discipline, DTO ≠ entity, validation at the edge** — unchanged from `CLAUDE.md`.

---

## Post-migration bridge (not this migration)

Once these five phases land:

- **Cross-account collaborative queues** — `Queue` becomes a per-room concept, not per-user. Out of MVP scope.
- **Smart queue (radio / autoplay after queue end)** — `AdvanceAfterEnd` plugs straight into a `IRecommendationProvider`.
- **Redis-backed session store** — swap `PostgresPlaybackSessionStore` → `RedisPlaybackSessionStore` if multi-instance api becomes a requirement. The `IPlaybackSessionStore` port already exists for this.
- **Apple Music queue items** — `Queue` already stores opaque IDs; a mixed-provider queue is a per-`Track`-resolution change, not a queue-model change.

Each of these is a contained change because the seams already exist after Phase 5.
