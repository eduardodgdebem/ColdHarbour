# Playback Concurrency Migration — Per-user serialized command pump + protocol revisioning

> Successor migration to `docs/PLAYBACK_MIGRATION.md` (server-authoritative playback). That migration made the server the source of truth; this one makes the server *correct under concurrency*. Today's `PlaybackSessionHub` has multiple data-race holes that begin manifesting the moment a user has more than one device connected (laptop + phone, two browser tabs, a transferred session mid-handoff). Each hole is benign in isolation and a heisenbug in production.
>
> Path from today's hub (raw `ConcurrentDictionary<Guid, ConcurrentBag<WebSocket>>`, mutable `PlaybackSession` aggregate handed out by reference from `IPlaybackSessionStore.GetOrCreate`, unconditional `SaveAsync` after every inbound frame, full-session broadcast on every heartbeat, no monotonic revision on outbound state) to a hub that **serializes every session mutation per user**, **revisions every outbound state message**, and **splits material updates from heartbeat ticks**.
>
> Five phases. Each phase ends with a **working, deployable system** — no phase breaks the app. The WS protocol grows incrementally and stays backward-compatible until the phase that explicitly retires the old shape.
>
> **TDD is mandatory** (see `CLAUDE.md` § "Working agreement"). Every domain method, command handler, hub branch, store method, and service method is preceded by a failing xUnit / Jasmine spec. Concurrency invariants are pinned by **stress specs** — N parallel commands from M simulated devices must leave the aggregate in a legal state. The 90 % minimum coverage holds throughout.
>
> **Branch + merge workflow.** Each phase happens on its own `playback-concurrency-phase-N-<slug>` branch and lands as one PR. Branches are cut **from `main`** after the prior phase merges — never stacked on an in-flight branch. A phase only merges to `main` after its *Definition of done* passes (tests, the concurrency stress spec for that phase, two-device manual smoke, the `CLAUDE.md` post-implementation checklist).
>
> **This file is the progress tracker.** When a phase completes, flip its row in the Status table to `✅ Done — landed on <branch> (<commit>) <YYYY-MM-DD>` in the same PR that lands the work. If the phase changed any architectural fact, update `CLAUDE.md` in the same PR.

## Status

| Phase | Title | Status |
|---|---|---|
| 1 | Per-user serialized command pump (silent) | ✅ Done — landed on playback-concurrency-phase-1-command-pump (a6efed0) 2026-05-28 |
| 2 | Connection-set primitive fix | ✅ Done — landed on playback-concurrency-phase-2-connection-set 2026-05-28 |
| 3 | `IPlaybackSessionStore` reshape (`LoadAsync` + `SaveAsync(snapshot, reason)`) | ✅ Done — landed on playback-concurrency-phase-3-store-reshape 2026-05-28 |
| 4 | Protocol revisioning + command acks | ⏳ Not started |
| 5 | Broadcast split (`tick` vs `state`) | ⏳ Not started |

---

## Concurrency holes this migration closes

These are the observable defects in `main` today. Each phase is anchored on at least one of them:

1. **No per-user serialization.** `PlaybackSession` has zero internal synchronization. `_queue` is a plain `List<Guid>`. Two devices firing `SetQueue` + `AddToQueue` (or `Next` + `RemoveFromQueue`) concurrently can corrupt `QueueIndex` or leave `TrackId` pointing at a track no longer in `Queue`. `PostgresPlaybackSessionStore.GetOrCreate` hands out a **live mutable reference**; multiple WS receive loops mutate it in parallel with no lock.
2. **Connection-bag races.** `PlaybackSessionHub._connections` is `ConcurrentDictionary<Guid, ConcurrentBag<WebSocket>>`. Both cleanup paths (disconnect, and dead-socket prune in `BroadcastToUserAsync`) do `TryGetValue` → rebuild bag → `TryUpdate` to drop a socket. Between the read and the update, a concurrent `bag.Add(ws)` on the original bag instance is silently lost.
3. **Unconditional `SaveAsync`.** Every inbound frame triggers a snapshot write, including malformed messages and no-op commands that didn't change the session. Heartbeat writes already had to be hand-throttled to 5 s in Phase 5 of the predecessor migration; the rest of the surface still writes blindly.
4. **No protocol revision / idempotency.** Each WS message is fire-and-forget. Client reconnects can replay commands. There is no monotonic `revision` clients can use to reorder/discard stale state, no `commandId` for idempotency, no `ack` for "command received, here's the outcome."
5. **Inefficient broadcasts.** Every message → full `PlaybackSessionDto` (queue can be 8 KB at 200 tracks) to every connected client. Heartbeats at 2 s × N tabs ≈ N² serializations per cycle. Hurts mobile networks and laptop battery.
6. **`IPlaybackSessionStore` port is unimplementable for Redis / multi-instance.** `GetOrCreate(userId) → PlaybackSession` returns a live mutable reference. A correct port is `LoadAsync(userId, ct) → snapshot` + `SaveAsync(snapshot, reason, ct)` with the in-memory cache owned by the actor, not the store.

---

## Phase 1 — Per-user serialized command pump (silent)

> Branch: `playback-concurrency-phase-1-command-pump`.

**Goal.** Every mutation of every `PlaybackSession` goes through a per-user single-consumer pump. No protocol change. No client change. The hub gets quieter and racier code paths become unreachable. This is the load-bearing phase — everything else builds on it.

**Changes.**

- **New type `PlaybackUserActor`** in `ColdHarbourBackend/src/ColdHarbour.Api/Playback/`. One instance per `UserId`. Owns:
  - a bounded `Channel<InboundCommand>` (writer = WS receive loops, single reader = the pump),
  - the in-memory `PlaybackSession` for that user (hydrated lazily on first command via the existing `IPlaybackSessionStore`),
  - a `CancellationTokenSource` for graceful shutdown.
- **New type `PlaybackUserActorRegistry`** (singleton, DI). `ConcurrentDictionary<Guid, PlaybackUserActor>` + `GetOrCreate(userId)`. The actor is created lazily, runs its pump on a background `Task`, and self-evicts after an idle timeout (configurable, default 5 min after the last command *and* zero open connections — guarded so a reconnect during the gap is safe).
- **`PlaybackSessionHub` rewrite (`ColdHarbourBackend/src/ColdHarbour.Api/Playback/PlaybackSessionHub.cs`):** WS receive loops no longer mutate `PlaybackSession` directly. Each parsed inbound frame becomes an `InboundCommand` record (sum type: `SetQueueCmd`, `NextCmd`, `PreviousCmd`, `SeekCmd`, `PauseCmd`, `ResumeCmd`, `HeartbeatCmd`, `TransferCmd`, `StopCmd`, `SetRepeatModeCmd`, `SetShuffleCmd`, `TrackEndedCmd`, `AddToQueueCmd`, `RemoveFromQueueCmd`, `ReorderQueueCmd`, `ClearQueueCmd`) and is `await`-written to the actor's channel. Malformed frames are rejected at parse time and never enter the channel.
- **Pump loop.** The actor reads commands one at a time, dispatches to the existing MediatR command handlers (or to the aggregate directly for hot-path commands — TBD by spec), then decides whether to persist + broadcast. **`SaveAsync` only runs when the command actually changed material state.** Heartbeats keep their existing 5 s throttle but the throttle now lives in the actor, not the store.
- **Broadcast still goes through the existing `BroadcastToUserAsync`** — protocol unchanged, so this phase is a no-op from the client's perspective.
- **MediatR handlers reshape.** Handlers in `ColdHarbour.Application/Playback` become pure session-mutation functions returning `(PlaybackSession next, bool changed)` instead of writing through the store themselves. The actor owns persistence. This isolates the concurrency story to one file per user.

**Concurrency invariants (pinned by stress specs).**

- 100 parallel `SetQueue` + `AddToQueue` + `Next` commands from two simulated devices leave `Queue`/`QueueIndex` consistent: `0 ≤ QueueIndex < Queue.Count` and `TrackId == Queue[QueueIndex]` (or both null).
- Commands enqueued from N WS receive loops are dispatched in FIFO order per user (channel guarantees this — spec asserts it).
- Actor eviction races: a command arriving during eviction either succeeds (actor was revived) or fails with a deterministic exception that the receive loop reports as a transient error, never silently dropped.

**Definition of done.**

- All existing playback tests still green. New specs in `ColdHarbour.Api.IntegrationTests/Playback/PlaybackUserActorTests.cs` cover the stress invariants above using `Testcontainers` Postgres.
- `git grep -n "lock (" ColdHarbourBackend/src/ColdHarbour.Domain/Playback` returns no new locks — the aggregate stays lock-free; serialization happens at the actor boundary.
- `git grep -n "SaveAsync" ColdHarbourBackend/src/ColdHarbour.Api/Playback/PlaybackSessionHub.cs` returns zero hits — the hub no longer persists.
- Manual two-device smoke: log in on Profile A and B, hammer next/previous/seek/setQueue from both simultaneously for 60 s; queue stays consistent in DevTools `session` payloads.
- ≥ 90 % coverage on `ColdHarbour.Api/Playback` and `ColdHarbour.Application/Playback`.

---

## Phase 2 — Connection-set primitive fix

> Branch: `playback-concurrency-phase-2-connection-set`.

**Goal.** Eliminate the bag-rebuild race in `PlaybackSessionHub._connections`. Disconnect cleanup and dead-socket prune become atomic. No protocol change.

**Changes.**

- **`PlaybackSessionHub.cs`:** replace `ConcurrentDictionary<Guid, ConcurrentBag<WebSocket>>` with `ConcurrentDictionary<Guid, ConcurrentDictionary<WebSocket, byte>>`. The inner dictionary is used as a set keyed on the `WebSocket` instance.
- **Add path:** `connections.GetOrAdd(userId, _ => new()).TryAdd(ws, 0)` — atomic, no rebuild.
- **Remove path:** `connections.TryGetValue(userId, out var set) && set.TryRemove(ws, out _)` — atomic, no rebuild. If `set.IsEmpty` after removal, attempt `connections.TryRemove(new KVP(userId, set))` (key-value overload so the racy "another socket joined in the meantime" case correctly *doesn't* delete the entry).
- **`BroadcastToUserAsync` dead-socket prune** uses the same atomic remove; no rebuild loop.
- **Actor coordination:** when the last connection for a user closes, signal the actor (via the registry) to allow eviction. When the first connection (re)opens, cancel any pending eviction. This re-uses the registry's idle-timeout machinery from Phase 1.

**Concurrency invariants (pinned by stress specs).**

- 100 parallel `connect` + `disconnect` cycles on the same user never lose an active socket from the broadcast set.
- 100 parallel disconnects on different sockets of the same user all succeed; the inner set is empty afterward.
- A disconnect racing with a new connect under the same `userId` does not orphan the new socket. Spec asserts the new socket receives the next broadcast.

**Definition of done.**

- New specs in `PlaybackSessionHubTests.cs` cover the three invariants above.
- `git grep -n "ConcurrentBag" ColdHarbourBackend/src/ColdHarbour.Api/Playback` returns zero hits.
- Manual smoke: open 6 tabs on the same account, refresh them in a tight loop for 30 s; the *current* tab still receives every broadcast (no "ghost" connections, no missed updates). Server logs show no `WebSocketException` from closed sockets.
- ≥ 90 % coverage on the hub.

---

## Phase 3 — `IPlaybackSessionStore` reshape (`LoadAsync` + `SaveAsync(snapshot, reason)`)

> Branch: `playback-concurrency-phase-3-store-reshape`.

**Goal.** Make `IPlaybackSessionStore` implementable for Redis or any multi-instance backend. The store returns **snapshots**, not mutable references. The in-memory cache moves into the actor (where access is already serialized).

**Changes.**

- **`ColdHarbour.Application/Playback/Ports/IPlaybackSessionStore.cs`:** replace
  ```
  PlaybackSession GetOrCreate(Guid userId);
  Task SaveAsync(PlaybackSession session, CancellationToken ct);
  ```
  with
  ```
  Task<PlaybackSession?> LoadAsync(Guid userId, CancellationToken ct);
  Task SaveAsync(PlaybackSession session, SaveReason reason, CancellationToken ct);
  ```
  where `SaveReason` is an enum: `Hydrate`, `MaterialChange`, `HeartbeatThrottled`, `Shutdown`. The reason is informational today (for logging + throttle decisions) and load-bearing for the Redis port later.
- **`ColdHarbourBackend/src/ColdHarbour.Infrastructure/Playback/PostgresPlaybackSessionStore.cs`:** drop the `ConcurrentDictionary` cache. `LoadAsync` reads from Postgres on every call (the actor caches the result for the lifetime of the user's session, so this is at most one read per actor lifetime). `SaveAsync` writes the snapshot. The 5 s heartbeat throttle moves out of the store into the actor — the store just persists what it's handed.
- **`ColdHarbour.Application.Tests/Playback/InMemoryPlaybackSessionStore.cs`:** updated to the new shape for tests. Returns a *clone* on `LoadAsync` so the aggregate identity rule (one writer = the actor) is enforced even in unit tests.
- **`PlaybackUserActor`:** on first command, calls `LoadAsync(userId)`; if null, constructs a fresh `PlaybackSession`. On material change, calls `SaveAsync(session, SaveReason.MaterialChange)`. On throttled heartbeat, `SaveAsync(..., HeartbeatThrottled)`. On idle eviction, `SaveAsync(..., Shutdown)`.
- **Wire-up in `Api/DependencyInjection.cs`:** unchanged binding (still `PostgresPlaybackSessionStore`), but the registration is now `Scoped` → `Singleton` if Phase 1's actor lifetime demands it (spec drives the choice).

**Concurrency invariants (pinned by stress specs).**

- After a `LoadAsync`, mutating the returned `PlaybackSession` does **not** affect a subsequent `LoadAsync` for the same user (clone semantics). Spec asserts this on both the in-memory and Postgres impls.
- `SaveAsync(session, MaterialChange)` followed by `LoadAsync` returns an equivalent session (round-trip property). Pinned by a `Testcontainers`-backed property spec.
- Two concurrent actors for the same user **must not exist** (registry invariant from Phase 1 — re-asserted here). If they ever did, `SaveAsync` is last-writer-wins; the spec documents this as a known limitation that a future Redis-with-optimistic-concurrency phase would address.

**Definition of done.**

- `git grep -n "GetOrCreate" ColdHarbourBackend/src/ColdHarbour.Application/Playback/Ports/IPlaybackSessionStore.cs` returns zero hits.
- All call sites of the port use `LoadAsync` / `SaveAsync(..., reason)`.
- `PostgresPlaybackSessionStoreTests` updated; round-trip property spec green.
- `docker compose restart api` while a track is playing on Profile A → both devices reconnect and see the same queue/index/position (regression coverage for Phase 5 of the predecessor migration).
- ≥ 90 % coverage on `Infrastructure/Playback` and `Application/Playback/Ports`.

---

## Phase 4 — Protocol revisioning + command acks

> Branch: `playback-concurrency-phase-4-protocol-revision`.

**Goal.** Outbound state messages carry a monotonic `revision`; inbound commands carry a client-generated `commandId`; the server emits an explicit `command-ack` for each. Clients can discard stale state and detect lost commands deterministically.

**Changes.**

- **Domain:** `PlaybackSession` gains `Revision: long` (initialized to 0, incremented on every material mutation by the actor — never by the aggregate directly, to keep the increment site singular). Phase 3 store snapshot schema gains a `revision bigint` column.
- **Application:** `PlaybackSessionDto` gains `revision: number` (`ColdHarbourBackend/src/ColdHarbour.Application/Playback/Dtos/PlaybackSessionDto.cs`).
- **Inbound messages:** every command type adds an optional `commandId: string` (client-generated UUID). Backward-compatible — missing `commandId` is treated as fire-and-forget (no ack expected, current behavior).
- **Outbound messages:**
  - Existing `{ type: "session", session: PlaybackSessionDto }` is renamed to `{ type: "state", session: PlaybackSessionDto }` *and* `session.revision` is populated. The old `session` type name is kept as an alias for one full phase to avoid mid-deploy breakage; removed in the Phase 5 PR.
  - New `{ type: "command-ack", commandId: string, status: "applied" | "noop" | "rejected", reason?: string, revision?: long }` emitted for every inbound command that carried a `commandId`. `revision` is populated when `status == "applied"` so the client knows which state message corresponds.
  - Existing `{ type: "devices", devices: DeviceDto[] }` unchanged.
- **Actor:** every material-change branch increments `Revision`, persists, then broadcasts `state` *and* unicasts `command-ack` to the originating socket. No-op commands (idempotent re-sends, duplicate commandIds within an actor lifetime — tracked in a small bounded LRU) emit `command-ack { status: "noop" }` without bumping revision or broadcasting.
- **Frontend (`ColdHarbourFrontend/src/app/features/player/services/playback-session.service.ts`):**
  - Track `localRevision: number = 0`. On incoming `state`, if `message.session.revision <= localRevision`, **discard** the message. Else set `localRevision = message.session.revision` and apply.
  - Generate a `commandId` for every outbound command. Maintain a small `pendingCommands: Map<commandId, { sentAt, type }>`. On incoming `command-ack`, resolve. Surface `rejected` to the UI as a transient toast (out of scope for this phase to design, but the hook is added).
  - Idempotent client retries: if a WS reconnects within 5 s of disconnect with `pendingCommands` non-empty, the client may resend them with the same `commandId`. The server's LRU dedupe makes this safe.

**Concurrency invariants (pinned by stress specs).**

- Revision is strictly monotonic per user. 1000 interleaved commands from 5 simulated devices produce `revision` values `1, 2, …, 1000` with no gaps and no duplicates.
- A `state` with `revision == localRevision` is dropped (echo from another tab's command).
- A duplicate `commandId` produces exactly one `applied` ack and any number of `noop` acks; never two `applied`.

**Definition of done.**

- `PlaybackSessionDto.revision` populated in every outbound `state` message; verified by integration spec.
- Frontend spec asserts that a backward-revision `state` is ignored and `localRevision` is not rolled back.
- A spec drops 30 % of WS frames at random, has the client retry pending commands on reconnect, and asserts the final state matches the no-loss baseline.
- ≥ 90 % coverage on the protocol surface (hub branches + service methods).

---

## Phase 5 — Broadcast split (`tick` vs `state`)

> Branch: `playback-concurrency-phase-5-broadcast-split`.

**Goal.** Stop sending 8 KB of queue every 2 s. Heartbeats become tiny `tick` messages; the full `state` ships only on material change. Cuts WS bandwidth ~10× on multi-tab users and trims CPU on the server.

**Changes.**

- **Outbound messages:**
  - `{ type: "state", session: PlaybackSessionDto }` — emitted only when material fields change: `queue`, `queueIndex`, `trackId`, `activeDeviceId`, `repeatMode`, `shuffle`, `isPlaying` flips, devices list changes. Carries the new `revision` (Phase 4).
  - `{ type: "tick", positionMs: number, isPlaying: boolean, revision: long }` — emitted on every heartbeat broadcast. Tiny payload (≤ 64 B). Does **not** bump `revision`; carries the current revision so the client can detect "we missed a state update."
- **Drop the deprecated `session` alias** from Phase 4 in this PR's first commit (it has had one full phase of co-existence).
- **Actor:** classify every command into `Material`, `Tick`, or `NoOp` at dispatch time. `Material` → save + bump revision + broadcast `state`. `Tick` (heartbeat only) → throttled save + broadcast `tick`. `NoOp` → ack-only.
- **Frontend (`playback-session.service.ts`):**
  - New `tick` handler updates `positionMs` and `isPlaying` signals only — does **not** touch queue/track/etc.
  - If incoming `tick.revision > localRevision`, the client knows it missed a `state`. Send a `{ type: "resync", lastSeenRevision: localRevision }` and the server replies with a fresh `state`. (Self-healing for the dropped-packet edge case; the WS spec doesn't drop frames in practice, but mobile network transitions do.)
  - Drift tolerance from the predecessor migration moves from the `session` handler to the `tick` handler — same 1 s threshold.
- **Hub messages (incoming):** add `{ type: "resync", lastSeenRevision: long, deviceId }` — actor responds with current `state`.

**Concurrency invariants (pinned by stress specs).**

- `tick` messages **never** carry queue/track changes. A spec mutates the queue mid-tick-burst and asserts the client's queue signal only updates from a `state`, never from a `tick`.
- `resync` returns the *current* state, not a historical replay. The client's `localRevision` after `resync` matches the server's actor `Revision`.
- Bandwidth spec (best-effort, recorded in CI as a benchmark not a gate): a 5-minute single-track playback with 4 connected tabs transmits < 20 KB of WS payload per tab. Today's baseline is ≈ 1.2 MB per tab over the same window.

**Definition of done.**

- `git grep -n '"session"' ColdHarbourBackend/src/ColdHarbour.Api/Playback` returns zero hits (alias retired).
- Frontend `tick` handler ships and is covered by Jasmine specs for: position update, isPlaying flip mid-tick, drift tolerance, `resync` on revision gap.
- Two-device smoke over 5 minutes: queue panel never flickers; position progresses smoothly; no audible glitches.
- ≥ 90 % coverage on the new message types (hub + service).

---

## Per-phase checklist (use on every PR)

See `CLAUDE.md` § "Post-implementation checklist" — apply to the feature landed in each phase. The bar is: the phase's *Definition of done* is met, the stress spec for that phase is green, and the checklist passes against the code delivered. Notable items for this migration specifically:

- **Active-device guard (`heartbeat`, `stop`)** remains intact — this migration does not relitigate the predecessor migration's Phase 2 narrowing. Transport commands still drop the guard; heartbeat still requires it.
- **DTO ≠ entity** — `PlaybackSession.Revision` is a domain field but `PlaybackSessionDto.revision` is the wire contract. They are not the same type and the mapper is exercised in specs.
- **No new endpoints, no new ports beyond the reshape** — all new messages ride `/ws/playback`. No `proxy.conf.json` or `Caddyfile` edits.
- **Coverage stays at ≥ 90 %** on both `ColdHarbourBackend` and `ColdHarbourFrontend`. Concurrency stress specs count toward this — they are real tests, not benchmarks.

---

## Post-migration bridge (not this migration)

Once these five phases land, the doors that this design opens:

- **Redis-backed session store** — `IPlaybackSessionStore` after Phase 3 is now Redis-implementable. Adding `RedisPlaybackSessionStore` is a contained change. Pair with optimistic concurrency on `revision` and the actor registry becomes shardable across api instances.
- **Multi-instance api** — once Redis lands, the actor registry becomes per-instance and a thin coordination layer (Redis stream of commands per user, or a consistent-hash router in Caddy) finishes the job. The per-user serialization invariant is preserved.
- **Command queue replay / offline support** — `commandId` + `revision` + `pendingCommands` already model an "outbox" on the client. A persistent outbox (IndexedDB) is a frontend-only change.
- **Audit log** — the actor sees every command in serialized order; appending a per-user audit stream is one method call away.

Each of these is a contained change because the seams already exist after Phase 5.
