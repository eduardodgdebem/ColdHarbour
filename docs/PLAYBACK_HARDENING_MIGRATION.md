# Playback Hardening Migration — Correctness, Efficiency, and Architectural Cleanups

> Catch-all follow-up to the three focused playback migrations: `docs/PLAYBACK_CONCURRENCY_MIGRATION.md`, `docs/PLAYEVENT_LIFECYCLE_MIGRATION.md`, `docs/WS_PROTOCOL_HARDENING_MIGRATION.md`. Those plans handle revision-numbered command serialization, the `PlayEvent.Begin/Complete` lifecycle, and tightening the WS message schema respectively. **This plan handles everything else** — a grab-bag of correctness, efficiency, and architectural cleanups that fall outside the three focused migrations.
>
> Six phases. Each phase ends with a **working, deployable system** — no phase breaks the app. Each is small enough to ship independently, but together they harden the playback stack for the multi-frontend control surface the playback migration already promises.
>
> **Independent of the focused migrations.** This plan can land in parallel with the concurrency, PlayEvent-lifecycle, and protocol-hardening plans. Where a phase touches a file those plans also touch (notably `PlaybackSession.cs` and `PlaybackSessionHub.cs`), the only constraint is the branch-per-phase + cut-from-`main` workflow below — whichever plan merges first wins the conflict, the other rebases.
>
> **TDD is mandatory** (see `CLAUDE.md` § "Working agreement"). Every domain method, command handler, hub branch, and service method is preceded by a failing xUnit / Jasmine spec. Behavior-level tests over mock-heavy unit tests. No "tests after." The 90 % minimum coverage holds throughout.
>
> **Branch + merge workflow.** Each phase happens on its own `playback-hardening-phase-N-<slug>` branch and lands as one PR. Branches are cut **from `main`** after the prior phase merges — never stacked on an in-flight branch. A phase only merges to `main` after its *Definition of done* passes (tests, manual smoke where called out, the `CLAUDE.md` post-implementation checklist). Per-phase branch lets a single phase be rolled back without dragging the others.
>
> **This file is the progress tracker.** When a phase completes, flip its row in the Status table to `✅ Done — landed on <branch> (<commit>) <YYYY-MM-DD>` in the same PR that lands the work. If the phase changed any architectural fact in `CLAUDE.md`, update it in the same PR.

## Status

| Phase | Title | Status |
|---|---|---|
| 1 | Liveness-aware active device + heartbeat gating | ✅ Done — landed on `playback-hardening-phase-1-liveness` 2026-06-12 |
| 2 | Frontend one-way data flow | ✅ Done — landed on `playback-hardening-phase-2-one-way` 2026-06-12 |
| 3 | Domain invariants + concurrency tests | ⏳ Not started |
| 4 | Server-side position interpolation + heartbeat sanity bound | ⏳ Not started |
| 5 | Domain events + outbox-style broadcasts | ⏳ Not started |
| 6 | Client-side command idempotency | ⏳ Not started |

---

## Reconciliation note (2026-06-12)

This plan predates the **actor refactor** (`PlaybackUserActor` + MediatR) and the WS-hardening migration. Reading it against current `main`:

- **The hub is a thin parser; all mutations run in the per-user `PlaybackUserActor` through a single-reader channel.** Wherever a phase says "the hub mutates the session and broadcasts," read that as "the actor dispatches a command/mutation through the pump." Anything that mutates `PlaybackSession` must go through the pump (single-writer), not from the hub's connect/disconnect path directly.
- **Phase 1 — done (adapted).** `PlaybackSession.DemoteActiveDevice()` added (releases ownership, keeps queue/track/position/`IsPlaying`). Liveness lives at the **actor chokepoint**, not inline in the hub: a `CheckLivenessCmd` (internal, enqueued by the hub on connect) and a pre-dispatch check ahead of every mutating command call `DemoteIfActiveDeviceStaleAsync` — demote only on positive evidence (active id set, not in `IConnectedDeviceStore`, device exists, `LastSeenAt` past `COLDHARBOUR_ACTIVE_DEVICE_TTL_SECONDS`, default 30). Unknown device / still-connected / recently-seen all fail safe (no demote). Recovery is the existing sender-claim rule: the next transport command from a live device demotes the dead owner then claims active. Heartbeat gating is enforced both client-side (`startHeartbeat` skips when `!isPlaying || trackId == null`) and server-side (actor `HeartbeatCmd` branch drops idle heartbeats).
- **Phase 6 — already implemented.** Client-side `commandId` idempotency is live: the frontend's `send()` attaches a `commandId` (UUID with the non-secure-context fallback) and the actor keeps a bounded per-user LRU (`_seenCommandIds`, cap 256), emitting a `noop` ack on duplicates. When Phase 6 is reached, verify against the actor's existing dedup rather than building `CommandIdempotencyStore` from scratch.
- **Phase 2 — done (adapted; schema change dropped).** Option 1 (add `playlistId` to the DTO + a snapshot column + migration) was **dropped**: `GetPlaylistQuery` ignores the id and returns the **whole library** as one "Library" playlist, so there is no real `Track → Playlist` owning relationship and the "playlist-1 is wrong for tracks not in playlist 1" bug does not exist (every track is in the library). Instead: the magic `setCurrentPlaylist(1)` is replaced by an honest `MusicService.loadLibrary()` (named `LIBRARY_PLAYLIST_ID` constant); the bidirectional `currentMusic` ↔ `setQueue` cycle is removed (deleted the echo effect + `applyingRemote` + `lastTrackId`), so the **server is the sole writer of `currentMusic`**; the row-click sites (`music-list`, home arrivals) now call the new public `PlaybackSessionService.setQueue(trackIds, startIndex)` explicitly (the component picks the list it is showing). The `connect()`/`disconnect()` race is closed two ways: `disconnect()` detaches the old socket's handlers before closing, and a per-connection `generation` counter makes a superseded socket's `onopen`/`onclose` a no-op. **Decision (user, 2026-06-12): the duplicate `/playlist/:id` page was removed entirely** (it was just another view of the library) — `LibraryPageComponent` at `/library` is the single library view; reintroduce a playlist page when a real playlist feature lands.

---

## Phase 1 — Liveness-aware active device + heartbeat gating

> Branch: `playback-hardening-phase-1-liveness`.

**Goal.** Stop leaving `ActiveDeviceId` pointing at a device that has gone away forever, and stop pumping heartbeats over the wire when there is nothing playing.

The Phase-5 durable-session work deliberately preserves session state across WS disconnects (`ApplyDisconnectPolicy` keeps `ActiveDeviceId` set so a reconnect resumes seamlessly). That trade is correct, but it has an obvious failure mode: if the active device disconnects and never returns, the session stays "owned" by a dead device. Transport commands target it, no audio is produced, and the user has to manually `transfer` to wake the session up.

Heartbeat gating is the small companion fix. The active device's WS client fires a `heartbeat` every 2 s whenever the socket is open, even when `IsPlaying = false` and `TrackId = null`. That is pure wire pollution that also forces the server to churn `UpdatedAt` for no reason.

**Changes.**

- **Liveness check at the boundary.** In `ColdHarbour.Api/Playback/PlaybackSessionHub.cs`, on every WS connect and on every incoming transport command, check whether `session.ActiveDeviceId` is null **or** still present in `InMemoryConnectedDeviceStore`. If the active device id is set but has no live socket and its `Device.LastSeenAt` is older than `N` seconds (`N = 30` initial; surface as `COLDHARBOUR_ACTIVE_DEVICE_TTL_SECONDS` so it's tunable without redeploy), demote `ActiveDeviceId = null` and broadcast the updated session.
- **Domain method.** Add `PlaybackSession.DemoteActiveDevice()` in `ColdHarbour.Domain/Playback/PlaybackSession.cs` — sets `ActiveDeviceId = null`, leaves queue/position/`IsPlaying` untouched (a freshly-claiming device should `transfer` at the existing position). Pair it with a domain spec covering the no-op case (active already null) and the demote-and-keep-position case.
- **Sender-claim still wins.** The existing "sender claims active when none" rule is the recovery path — once `ActiveDeviceId` is demoted, the next pause/resume/next/previous/seek from any device naturally takes ownership. No new "force-takeover" message is needed.
- **Heartbeat gate (frontend).** In `ColdHarbourFrontend/src/app/features/player/services/playback-session.service.ts`, the heartbeat tick should send only when `session().isPlaying === true` **and** `session().trackId != null`. Idle WS stays open but silent.
- **Heartbeat gate (server defence-in-depth).** In the hub's `heartbeat` branch, drop the heartbeat silently if `session.IsPlaying = false` or `session.TrackId = null`. Stops a misbehaving client from undoing the frontend gate.

**Definition of done.**

- New xUnit spec `PlaybackSessionHubTests.DemotesStaleActiveDeviceOnConnect` proves that connecting Device B while Device A's record is older than the TTL and not in the connected store demotes `ActiveDeviceId` to null and broadcasts the change.
- New domain spec `PlaybackSessionTests.DemoteActiveDevice_LeavesPositionAndQueueIntact`.
- New Jasmine spec on `PlaybackSessionService` proving heartbeat is skipped when `isPlaying === false` and emitted when `true`.
- **Manual smoke.** Play a track on Device A, then `kill -9` the browser tab (do not graceful-close). Wait ≥ `N` seconds. Send `pause` from Device B — it is accepted, Device B becomes active, and the session unfreezes. Without this phase the command sits silent against the dead Device A.
- All test suites ≥ 90 %.

---

## Phase 2 — Frontend one-way data flow

> Branch: `playback-hardening-phase-2-one-way`.

**Goal.** Make the server the sole writer to `musicService.currentMusic`. Rip out the `applyingRemote` / `lastTrackId` complexity that exists only to break the cycle, and stop hardcoding playlist `1` as a fallback. Fix a `connect()`/`disconnect()` race that the cycle has been hiding.

Today `PlaybackSessionService` has a bidirectional binding: the server-state effect writes `musicService.currentMusic`, and a separate effect listens to `musicService.currentMusic` to push `setQueue` back to the server. The `applyingRemote` flag exists to short-circuit the second effect when the first one wrote. This is accidental complexity — exactly the pattern signals are supposed to remove.

The playlist-1 fallback is broken for any user whose currently-playing track is not in playlist 1, which is most of them once the library grows.

The `connect()`/`disconnect()` race only fires when the WS bounces fast: `disconnect()` sets `this.ws = undefined` synchronously and the old socket's `onclose` then runs `stopHeartbeat()` against the *new* socket's just-started heartbeat. Easy to miss until it happens in production.

**Changes.**

- **Single writer.** In `ColdHarbourFrontend/src/app/features/player/services/playback-session.service.ts`, keep the server-state-effect that writes `musicService.currentMusic` and **delete** the effect that watches `musicService.currentMusic` to send `setQueue`. Delete `applyingRemote` and `lastTrackId`.
- **Explicit push at the call site.** Wherever a user picks a track from a playlist (in `MusicService.selectMusic`, in `/playlist/:id` row clicks, in `/library`), call `playbackSessionService.setQueue(trackIds, startIndex)` **directly** instead of mutating `musicService.currentMusic` and letting the effect react. `MusicService.selectMusic` becomes a thin wrapper that calls `setQueue` and lets the server-state effect write `currentMusic` on the broadcast echo.
- **Drop the `setCurrentPlaylist(1)` bootstrap.** When the WS session has a `trackId` but the frontend has no playlist loaded, do **not** assume playlist 1. Two options, pick one:
  1. Extend `PlaybackSessionDto` with `playlistId?: Guid` populated from the session (set by `setQueue` and persisted in the snapshot store). Frontend uses that to call `setCurrentPlaylist(playlistId)`.
  2. Keep the DTO unchanged and add a fallback REST call: `GET /api/library/track/{trackId}` returns the track + its containing playlist id. Frontend resolves once on hydration.
  Choose option 1 unless `Track → Playlist` is not yet a single owning relationship in the schema. If a track can belong to multiple playlists, the server still picks one (most-recently-added wins) and the frontend treats it as a hint, not a binding.
- **`connect()`/`disconnect()` race.** In `PlaybackSessionService.disconnect()`, null out the old socket's handlers before closing: `ws.onclose = null; ws.onmessage = null; ws.onerror = null; ws.close();`. Alternatively (and additionally — defence in depth) carry a per-connection generation counter; every `onclose` checks `if (this.generation !== closedGeneration) return;` before touching shared state.
- **Heartbeat ownership stays on the service.** This phase does not touch heartbeat scheduling beyond what Phase 1 already did.

**Changes touch (concrete files).**

- `ColdHarbourFrontend/src/app/features/player/services/playback-session.service.ts`
- `ColdHarbourFrontend/src/app/features/player/services/music.service.ts`
- `ColdHarbourFrontend/src/app/features/player/pages/playlist-page/playlist-page.component.ts` (and any other row-click site that today mutates `currentMusic` directly)
- If option 1 is chosen: `ColdHarbour.Application/Playback/PlaybackSessionDto.cs`, `PlaybackSession.SetQueue(...)` signature gains `playlistId`, snapshot column added in a tiny `0005_PlaybackSessionPlaylistId` migration. If option 2 is chosen: `ColdHarbour.Api/Controllers/LibraryController.cs` gains the lookup endpoint.

**Definition of done.**

- `git grep -n "applyingRemote\\|lastTrackId" ColdHarbourFrontend/src` returns no live references.
- `git grep -n "setCurrentPlaylist(1)" ColdHarbourFrontend/src` returns no references.
- New Jasmine spec `playback-session.service.spec.ts > does not echo setQueue when server-state effect writes currentMusic` proves the cycle is gone.
- New Jasmine spec proves rapid `connect → disconnect → connect` does not leak a stale `onclose` into the new socket's lifecycle (mock `WebSocket`, assert `stopHeartbeat` is not called against the new connection).
- **Manual smoke.** Sign in on a fresh device whose last-played track lives in playlist 17 (not 1). Reload. The player hydrates with the correct queue context — no flicker of an unrelated playlist, no missing queue.
- All test suites ≥ 90 %.

---

## Phase 3 — Domain invariants + concurrency tests

> Branch: `playback-hardening-phase-3-invariants`.

**Goal.** Centralize the "claim active when none + apply transport" rule that today lives copy-pasted across six hub call sites, put a domain-level cap on queue size, and add a property-style concurrency test that fans out random commands across multiple simulated devices to assert invariants hold.

The "sender claims active when none" rule currently lives in the hub's pause, resume, setQueue, next, previous, seek, addToQueue, removeFromQueue, reorderQueue, and clearQueue branches. Every new transport command has to remember to call `ClaimActiveIfNone(senderDeviceId)`. That is exactly the kind of seam that grows a bug the moment a contributor forgets. Push it into the domain.

No queue-size cap means clients can push a 10 000-track queue. The whole queue is JSON-serialized into Postgres on every snapshot, broadcast to every subscribed client, and held in memory forever. A cap turns a runaway client into a benign 4xx instead of a memory blow-up.

There are zero tests today that fire concurrent commands from two devices and assert invariants. Every focused migration (concurrency, lifecycle, protocol) touches the same aggregate. A property-style test catches regressions across all three.

**Changes.**

- **Centralize.** Add `PlaybackSession.ApplyTransport(senderDeviceId, Action mutate)` in `ColdHarbour.Domain/Playback/PlaybackSession.cs`. Method body:
  1. `ClaimActiveIfNone(senderDeviceId)` (existing logic, moved inside).
  2. Invoke `mutate()` (the per-command state change — e.g. `Pause()`, `Resume()`, `AdvanceNext()`, `Seek(positionMs)`, `SetQueue(...)`).
  3. Bump `UpdatedAt`.
  Every hub branch in `ColdHarbour.Api/Playback/PlaybackSessionHub.cs` calls `session.ApplyTransport(senderDeviceId, () => session.Pause())` (or equivalent) instead of duplicating the claim-and-mutate dance. Application command handlers in `ColdHarbour.Application/Playback/` route through the same method.
- **Queue cap.** Add a domain-level constant `PlaybackSession.MaxQueueSize = 1000`. `SetQueue` and `AddToQueue` throw `QueueTooLargeException` when the resulting `Queue.Count > MaxQueueSize`. Hub maps the domain exception to a `{ type: "error", code: "queue_too_large", limit: 1000 }` server-to-client message and does not mutate session. Frontend toasts the error and aborts the operation.
- **Concurrency test.** New file `ColdHarbour.Application.Tests/Playback/ConcurrentCommandPropertyTests.cs`. Test method runs 50 randomly-ordered commands (mix of `setQueue`, `next`, `previous`, `seek`, `pause`, `resume`, `addToQueue`, `removeFromQueue`, `reorderQueue`, `setShuffle`, `setRepeatMode`, `trackEnded`) fired from 2–3 simulated device ids through the real command handlers against a real `InMemoryPlaybackSessionStore`. After the run, assert the invariants:
  - `Queue.Count == 0` ⇒ `TrackId == null` and `IsPlaying == false`.
  - `Queue.Count > 0` ⇒ `0 ≤ QueueIndex < Queue.Count`.
  - `TrackId == null` or `TrackId == Queue[QueueIndex]`.
  - `Queue.Count ≤ MaxQueueSize`.
  - `ActiveDeviceId == null` or `ActiveDeviceId ∈ knownDeviceIds`.
  Run with at least 50 random seeds (xUnit `[Theory]` + `[InlineData]` over seeds). Each failing seed must be reproducible from the seed alone — log the seed on assertion failure.

**Changes touch (concrete files).**

- `ColdHarbourBackend/src/ColdHarbour.Domain/Playback/PlaybackSession.cs`
- `ColdHarbourBackend/src/ColdHarbour.Api/Playback/PlaybackSessionHub.cs`
- `ColdHarbourBackend/src/ColdHarbour.Application/Playback/*Handler.cs` (every transport handler)
- `ColdHarbourBackend/tests/ColdHarbour.Application.Tests/Playback/ConcurrentCommandPropertyTests.cs` (new)
- Frontend: a small toast/error path in `PlaybackSessionService` for the new `queue_too_large` error code.

**Definition of done.**

- `git grep -n "ClaimActiveIfNone" ColdHarbourBackend/src` returns hits only in `PlaybackSession.cs` (definition + `ApplyTransport` call site). Hub no longer references it directly.
- New domain spec `PlaybackSessionTests.ApplyTransport_ClaimsActiveWhenNone_AndAppliesMutation`.
- New domain spec `PlaybackSessionTests.SetQueue_ThrowsWhenExceedsMaxQueueSize`.
- New property test `ConcurrentCommandPropertyTests` runs ≥ 50 seeds, all green; each invariant has a dedicated assertion line so failures localise.
- All test suites ≥ 90 %.

---

## Phase 4 — Server-side position interpolation + heartbeat sanity bound

> Branch: `playback-hardening-phase-4-position`.

**Goal.** Give REST callers an accurate "now playing" position without forcing them onto the WS, and stop accepting heartbeats whose `positionMs` is too far away from what the server expects.

Today the server's `PositionMs` is exactly the last-received heartbeat value. Between heartbeats (up to 2 s) the server has no idea where playback is. A future "now playing" REST endpoint, an Apple Music adapter that needs to align its remote state against the server's, or a long-poll fallback for non-WS clients all need an interpolated position — not the last frozen sample.

The sanity bound closes the other direction. A heartbeat that arrives with `positionMs = 9_999_999` (rogue process, replayed packet, debugger pause-skipping the audio element forward) currently teleports the session and every subscriber re-seeks to the bad value. Bound the accepted range to `[lastPositionMs, lastPositionMs + grace]` where `grace ≈ heartbeatInterval × 2.5` (5 s for a 2 s heartbeat).

**Changes.**

- **`CurrentPositionMs` derived property.** Add to `PlaybackSession`:
  ```
  public long CurrentPositionMs(DateTimeOffset now)
      => IsPlaying
         ? PositionMs + (long)(now - UpdatedAt).TotalMilliseconds
         : PositionMs;
  ```
  No state change; pure read. Exposed in `PlaybackSessionDto` as `currentPositionMs` (still alongside the canonical `positionMs` + `updatedAt` so clients can interpolate themselves if they want).
- **REST surface.** New `GET /api/playback/session` endpoint (Application: `GetActiveSessionQuery`). Returns the DTO with `currentPositionMs` computed from `DateTimeOffset.UtcNow`. Authorized, rate-limited under the same bucket as other read endpoints.
- **Heartbeat sanity bound.** In the hub's `heartbeat` branch (and inside `PlaybackSession.RecordHeartbeat(positionMs, now)`), reject any heartbeat where `positionMs < lastPositionMs - 250` (small back-tolerance for clock skew on the active device) or `positionMs > lastPositionMs + 5000`. Log at `Information` with the device id, accepted range, and offered value. Do not throw — silently drop and do not bump `UpdatedAt`. Configurable upper bound via `COLDHARBOUR_HEARTBEAT_MAX_DRIFT_MS` (default 5000).
- **`trackEnded` is exempt.** The `trackEnded` handler runs `AdvanceAfterEnd` and explicitly sets `PositionMs = 0` for the new track. That is not a heartbeat — it does not pass through the sanity bound.

**Changes touch (concrete files).**

- `ColdHarbourBackend/src/ColdHarbour.Domain/Playback/PlaybackSession.cs` — `CurrentPositionMs`, `RecordHeartbeat` bound.
- `ColdHarbourBackend/src/ColdHarbour.Application/Playback/PlaybackSessionDto.cs` — `currentPositionMs` field.
- `ColdHarbourBackend/src/ColdHarbour.Application/Playback/GetActiveSessionQuery.cs` (existing query) — populate the new field.
- `ColdHarbourBackend/src/ColdHarbour.Api/Controllers/PlaybackController.cs` — new `GET /api/playback/session` (or extend the existing one if present).
- `ColdHarbourBackend/src/ColdHarbour.Api/Playback/PlaybackSessionHub.cs` — wire the rejected-heartbeat log.
- `ColdHarbourBackend/src/ColdHarbour.Infrastructure/Playback/{Postgres,InMemory}PlaybackSessionStore.cs` — no change (derived property, not persisted).

**Definition of done.**

- New domain spec `PlaybackSessionTests.CurrentPositionMs_InterpolatesWhilePlaying` (asserts ~500 ms after a `Resume` from position 10 000, `CurrentPositionMs(now+500ms) ≈ 10500` within 50 ms slack; with `IsPlaying = false`, the value does not advance).
- New domain spec `PlaybackSessionTests.RecordHeartbeat_RejectsOutOfBoundsValue`.
- New integration spec hitting `GET /api/playback/session` after a 1 s wait — returned `currentPositionMs` is approximately `lastHeartbeatPositionMs + 1000` while playing.
- **Manual smoke.** Open `/api/playback/session` in a second tab; watch `currentPositionMs` advance between heartbeats without polling the WS.
- All test suites ≥ 90 %.

---

## Phase 5 — Domain events + outbox-style broadcasts

> Branch: `playback-hardening-phase-5-domain-events`.

**Goal.** Stop relying on every mutation site to remember to broadcast. Today the hub manually calls `BroadcastSession(...)` after each message handler. If a REST endpoint, a scheduled job, or a future Apple Music adapter mutates the session and forgets the broadcast, every subscribed client silently goes stale.

This is the same domain-events pattern used elsewhere in DDD: the aggregate records what changed; an outbox flushes the side effects in one place.

**Changes.**

- **Domain event base.** Add `DomainEvent` record type in `ColdHarbour.Domain/Common/DomainEvent.cs` (if not already present). Add `IHasDomainEvents` interface with `IReadOnlyCollection<DomainEvent> DomainEvents { get; }` and `void ClearDomainEvents()`. `PlaybackSession` implements it.
- **Concrete events.** `SessionChanged(UserId, PlaybackSessionSnapshot)`, `DeviceListChanged(UserId)`. Raised inside every state-mutating method of `PlaybackSession` (`ApplyTransport`, `RecordHeartbeat` when accepted, `DemoteActiveDevice`, etc.). One event per logical change — `ApplyTransport` raises exactly one `SessionChanged` regardless of how many internal helpers fire.
- **Outbox / dispatcher.** In `ColdHarbour.Infrastructure/Playback/`, add `PlaybackSessionEventDispatcher` (MediatR notification publisher). After every `IPlaybackSessionStore.SaveAsync(session)` call, the store reads `session.DomainEvents`, dispatches each one through MediatR, and clears them. Two notification handlers:
  - `SessionChangedBroadcastHandler` — sends the `{ type: "session", session: dto }` WS broadcast to every subscriber of `UserId`.
  - `DeviceListChangedBroadcastHandler` — sends the `{ type: "devices", devices: [...] }` broadcast.
- **Hub diet.** `PlaybackSessionHub.BroadcastSession(...)` is **deleted**. The hub becomes a thin protocol-translation layer: parse incoming WS message → invoke the matching MediatR command → done. The broadcast happens automatically when the command's handler saves the session.
- **No new endpoints in this phase.** This is pure refactoring — the public protocol is unchanged. The win is that the next contributor who adds a REST endpoint that mutates the session gets the broadcast for free.

**Changes touch (concrete files).**

- `ColdHarbourBackend/src/ColdHarbour.Domain/Common/DomainEvent.cs` (new, if not present).
- `ColdHarbourBackend/src/ColdHarbour.Domain/Playback/PlaybackSession.cs` — raise events.
- `ColdHarbourBackend/src/ColdHarbour.Domain/Playback/Events/{SessionChanged,DeviceListChanged}.cs` (new).
- `ColdHarbourBackend/src/ColdHarbour.Infrastructure/Playback/{Postgres,InMemory}PlaybackSessionStore.cs` — dispatch on save.
- `ColdHarbourBackend/src/ColdHarbour.Infrastructure/Playback/PlaybackSessionEventDispatcher.cs` (new).
- `ColdHarbourBackend/src/ColdHarbour.Infrastructure/Playback/Handlers/{SessionChangedBroadcastHandler,DeviceListChangedBroadcastHandler}.cs` (new).
- `ColdHarbourBackend/src/ColdHarbour.Api/Playback/PlaybackSessionHub.cs` — strip the broadcast calls; keep only protocol parsing and command dispatch.

**Definition of done.**

- `git grep -n "BroadcastSession\\|BroadcastDevices" ColdHarbourBackend/src` returns no live references inside the hub.
- New unit spec `PlaybackSessionTests.ApplyTransport_RaisesSessionChangedExactlyOnce`.
- New integration spec `PlaybackSessionEventDispatcherTests.DispatchesPendingEventsAfterSave` (uses a fake `ISender` to capture published notifications).
- Existing two-device smoke test (a transport command on B updates A's UI) still passes — the test path is unchanged from the client's perspective.
- All test suites ≥ 90 %.

---

## Phase 6 — Client-side command idempotency

> Branch: `playback-hardening-phase-6-idempotency`.

**Goal.** A retried command (network blip, optimistic UI nudge, double-click on a slow button) must not double-apply. This is a smaller, self-sufficient alternative to the full revision-numbering work in `PLAYBACK_CONCURRENCY_MIGRATION.md` — they compose, neither blocks the other.

The client already tracks per-click intent locally. If it tags each outgoing command with a `commandId` (UUIDv4, generated at the call site), the server can hold a small LRU of recently-seen ids per user and silently drop duplicates. That covers the bulk of double-fire bugs without touching the durable-session revision counter.

**Changes.**

- **WS message extension.** Every client → server transport message gets an optional `commandId: string` field. Existing message types stay backward-compatible — the server treats missing `commandId` as "always apply" (legacy path; pre-Phase-6 clients keep working).
- **Server LRU.** `ColdHarbour.Application/Playback/CommandIdempotencyStore.cs` — `ConcurrentDictionary<UserId, BoundedQueue<string>>` of recent command ids, bounded at 128 per user. Before each handler invocation, the hub checks: if `commandId` is non-null and already in the user's queue, **skip the handler and re-broadcast the current session** (so the duplicate-sender's UI still converges if it had drifted). Otherwise enqueue + dispatch.
- **Frontend integration.** In `ColdHarbourFrontend/src/app/features/player/services/playback-session.service.ts`, every send method generates `commandId = crypto.randomUUID()` (with the existing `generateUUID()` fallback for non-secure-context contexts noted in `CLAUDE.md` hurdle 7) and attaches it to the payload. No retry policy is added in this phase — the field is there for retries to lean on later.
- **Bound + eviction.** When the per-user queue exceeds 128, drop the oldest entry. The window is intentionally tight: idempotency is about the immediate retry, not long-term replay protection (which `WS_PROTOCOL_HARDENING_MIGRATION.md` covers with revisions).
- **No persistence.** The LRU is in-memory only. On api restart, the window is empty — that is fine; the worst case after a restart is one duplicated command per user, which is no worse than today.

**Changes touch (concrete files).**

- `ColdHarbourBackend/src/ColdHarbour.Application/Playback/CommandIdempotencyStore.cs` (new).
- `ColdHarbourBackend/src/ColdHarbour.Application/Playback/DependencyInjection.cs` — register as singleton.
- `ColdHarbourBackend/src/ColdHarbour.Api/Playback/PlaybackSessionHub.cs` — pre-handler check + re-broadcast on duplicate.
- `ColdHarbourFrontend/src/app/features/player/services/playback-session.service.ts` — attach `commandId` to every send.

**Definition of done.**

- New unit spec `CommandIdempotencyStoreTests.{ReturnsTrueOnFirstSeen,ReturnsFalseOnRepeat,EvictsOldestPast128}`.
- New integration spec on the hub: sending the same `commandId` twice in succession results in **one** state mutation and **two** session broadcasts (the second is the re-broadcast for the duplicate sender's convergence).
- New Jasmine spec proving every `PlaybackSessionService` send method attaches a `commandId`.
- **Manual smoke.** Double-click the Next button on `/player` over a throttled connection. Verify the queue advances by exactly one position (today it occasionally advances by two).
- All test suites ≥ 90 %.

---

## Per-phase checklist (use on every PR)

See `CLAUDE.md` § "Post-implementation checklist" — apply to the feature landed in each phase. The bar is: the phase's *Definition of done* is met, and the checklist passes against the code delivered. Notable items for this migration specifically:

- **Active-device guard:** unchanged from `PLAYBACK_MIGRATION.md` Phase 2 — `heartbeat` and `stop` stay guarded; transport commands stay open. Phase 1's *demotion* of `ActiveDeviceId` is not a relaxation of the guard, it is recovery from a stale value.
- **Layer discipline:** the `ApplyTransport` centralization (Phase 3), domain events (Phase 5), and idempotency store (Phase 6) all stay on the correct side of `Domain → Application → Infrastructure`. The store is an Application-layer service, not Infrastructure.
- **No absolute origins / no hardcoded localhost:** every change rides the existing `/ws/playback` socket or the existing `/api/playback/*` REST surface. No new proxy.conf or Caddyfile entries.
- **Cookie Secure flag, DTO ≠ entity, validation at the edge** — unchanged from `CLAUDE.md`.

---

## Post-migration bridge (not this migration)

Once these six phases land:

- **Full revision-numbered concurrency** — `PLAYBACK_CONCURRENCY_MIGRATION.md` builds on Phase 6's `commandId` foundation, layering a server-monotonic revision counter on top for true causal ordering.
- **`now playing` for embedders** — Phase 4's `GET /api/playback/session` is the seam an Apple TV / dashboard widget consumes without touching the WS protocol.
- **Outbox persistence for cross-instance broadcasts** — if multi-instance api becomes a requirement, Phase 5's in-process dispatcher becomes a persistent outbox table polled from each instance. The seam is already there.
- **Per-user observability** — the idempotency LRU (Phase 6) is one signal away from a "commands per user per minute" metric for the existing Serilog pipeline.

Each of these is a contained change because the seams already exist after Phase 6.
