# PlayEvent Lifecycle Migration — Handler-scattered open/close → Aggregate-owned timeline

> Path from today's broken `PlayEvent` lifecycle (every transport command opens an event, only `trackEnded` closes one, six handlers each duplicate the logic, the client supplies its own track duration) to a model where a single **`PlaySessionTimeline`** centralizes open/close, pause/resume contributes to listened-time, and durations come from the server. Closes the silent corruption of every analytic built on `PlayEvent` (`PlaybackStatsJob` weekly aggregates, completion ratios, listen-time totals).
>
> Five phases. Each phase ends with a **working, deployable system** — no phase breaks the app. Domain invariants are tested first (red), then enforced; the timeline grows incrementally so phases roll out sequentially and must remain compatible with whatever is on `main` at every commit.
>
> **TDD is mandatory** (see `CLAUDE.md` § "Working agreement"). Every domain method, command handler, and service method is preceded by a failing xUnit / Jasmine spec. No "tests after." The 90 % minimum coverage holds throughout. Behavior-level assertions (e.g. "after N random transport commands, at most one open `PlayEvent` exists") are preferred over mock-heavy unit tests.
>
> **Branch + merge workflow.** Each phase happens on its own `playevent-phase-N-<slug>` branch and lands as one PR. Branches are cut **from `main`** after the prior phase merges — never stacked on an in-flight branch. A phase only merges to `main` after its *Definition of done* passes (tests, integration assertions, the `CLAUDE.md` post-implementation checklist).
>
> **This file is the progress tracker.** When a phase completes, flip its row in the Status table to `✅ Done — landed on <branch> (<commit>) <YYYY-MM-DD>` in the same PR that lands the work. If the phase changed any architectural fact, update `CLAUDE.md` in the same PR.

## Status

| Phase | Title | Status |
|---|---|---|
| 1 | Invariant tests (red contract) | Pending |
| 2 | `PlaySessionTimeline` centralization | Pending |
| 3 | Pause-aware listened-time accumulation | Pending |
| 4 | Server-trusted duration on `trackEnded` | Pending |
| 5 | Orphan backfill + stats re-runnability | Pending |

---

## The problem (why this migration exists)

`PlayEvent` is supposed to be the durable record of what the user actually listened to. Today the lifecycle is broken in six concrete ways:

1. **Orphaned events on `next` / `previous`.** `NextTrackCommandHandler` and `PreviousTrackCommandHandler` call `PlayEvent.Begin(...)` without closing the prior event. Five clicks on Next → five rows with `EndedAt = null`. `IPlayEventRepository.FindActiveByUserAsync` returns `OrderByDescending(StartedAt).First()` so older opens are silently abandoned forever.
2. **`SetQueueCommandHandler` and `AddToQueueCommandHandler` open without closing.** Same shape of leak. Every playlist switch leaks an event.
3. **`TransferPlaybackCommandHandler` doesn't close the previously-active device's open event.** The new active device's next mutation opens another. Device attribution on long-running events becomes a lie.
4. **Pause / Resume don't pause the event.** A user paused for an hour has a `PlayEvent` whose `StartedAt → EndedAt` window includes the hour they weren't listening. `PlaybackStatsJob` aggregates wall-clock time, not listened-time.
5. **`TrackEndedCommand` accepts client-supplied `DurationMs`.** A client can claim a 30 s track played for 30 min and stats record it verbatim. The server already has `Track.Duration` — it should be the source of truth.
6. **Lifecycle logic lives in handlers, not the aggregate.** Six handlers duplicate "open a new `PlayEvent`" decisions; the `PlaybackSession` aggregate has no idea `PlayEvent` exists. Spec violation of rich-DDD guideline #6 in `CLAUDE.md`.

The invariants we want at the end:

- **At most one open `PlayEvent` per user at any time.**
- **Every change of `PlaybackSession.TrackId` closes the prior event** before opening a new one.
- **Every change of `PlaybackSession.ActiveDeviceId` closes the prior event** (device attribution is honest).
- **`PlayEvent.ListenedMs` reflects time actually playing**, not wall-clock between `StartedAt` and `EndedAt`.
- **`PlayEvent.Complete` durations come from the server**, not the client payload.
- **`PlaybackStatsJob` is idempotent** — running it twice over the same window yields identical aggregates.

---

## Phase 1 — Invariant tests (red contract)

> Branch: `playevent-phase-1-invariants`.

**Goal.** Author the full xUnit contract for the desired lifecycle as **failing tests** against the current code. No production change. The red suite becomes the spec for every phase that follows.

**Changes.**

- New test class `ColdHarbour.Application.Tests/Playback/PlayEventLifecycleTests.cs` covering, against the current handler stack wired with the in-memory `IPlayEventRepository`:
  - `SetQueue → SetQueue` produces exactly one open event for the user.
  - `SetQueue → Next` closes the first event and opens a second (DB has one open, one closed).
  - `SetQueue → Next × 100` produces exactly one open event and 100 closed events.
  - `SetQueue → Previous × 50` produces exactly one open event and 50 closed events.
  - `SetQueue (deviceA) → Transfer(deviceB)` closes the event opened by `deviceA` (with `DeviceId = deviceA`) and opens a fresh one attributed to `deviceB`.
  - `SetQueue → AddToQueue (advancing the current track)` closes prior, opens next.
  - `SetQueue → Pause → Resume` does **not** open a second event (Phase-3 invariant tested in Phase 1 as red).
  - `SetQueue → Pause (1 h) → TrackEnded` records `ListenedMs` excluding the paused hour (Phase-3 invariant tested in Phase 1 as red).
  - `TrackEnded` with a client-supplied `DurationMs` that exceeds `Track.Duration` is rejected or clamped to `Track.Duration` (Phase-4 invariant tested in Phase 1 as red).
  - **Random-walk behavior test:** 100 random transport commands (`SetQueue`, `Next`, `Previous`, `Seek`, `Pause`, `Resume`, `Transfer`, `AddToQueue`, `RemoveFromQueue`, `TrackEnded`) across 3 fake devices. Final assertion: `repository.CountOpenByUserAsync(userId)` ≤ 1.
- New domain test `ColdHarbour.Domain.Tests/Playback/PlayEventTests.cs` for any new domain methods the contract implies (e.g. `PlayEvent.PauseListening`, `PlayEvent.ResumeListening`, `PlayEvent.Complete(durationMs)`) — these classes/methods do not exist yet; the tests reference the intended shape and stay red.
- The random-walk test must be deterministic (seeded `Random`) so failures bisect cleanly.

**Definition of done.**

- New test file(s) compile (intended-shape stubs for not-yet-existing methods are fine — guard them behind `Skip` only if compilation requires it; prefer real `Assert.Fail` red).
- `cd ColdHarbourBackend && dotnet test --filter FullyQualifiedName~PlayEventLifecycle` is **red** for the expected reasons documented in each test's `// ARRANGE` comment. No green assertions hiding a real failure.
- `dotnet test` outside the new filter remains green — the rest of the suite is unaffected.
- PR description lists exactly which invariants are red and links each to the phase that will turn it green.

---

## Phase 2 — `PlaySessionTimeline` centralization

> Branch: `playevent-phase-2-timeline`.

**Goal.** Move open/close logic out of every command handler and into one place. Every domain mutation of `PlaybackSession.TrackId` or `PlaybackSession.ActiveDeviceId` flows through the timeline. Phase 1's track-change, transfer, and random-walk tests turn green.

**Changes.**

- **New port `ColdHarbour.Application/Playback/Ports/IPlaySessionTimeline.cs`:**
  ```csharp
  Task TrackChangedAsync(Guid userId, Guid deviceId, Guid? oldTrackId, int oldPositionMs, Guid? newTrackId, CancellationToken ct);
  Task ActiveDeviceChangedAsync(Guid userId, Guid? oldDeviceId, int oldPositionMs, Guid? newDeviceId, CancellationToken ct);
  Task SessionClearedAsync(Guid userId, int oldPositionMs, CancellationToken ct);
  ```
- **New implementation `ColdHarbour.Application/Playback/Services/PlaySessionTimeline.cs`** (lives in Application so command handlers depend on the interface and the impl coordinates `IPlayEventRepository`; no Infrastructure leak — the repository is already an Application port).
  - `TrackChangedAsync` closes the open event for `userId` (if any) via `PlayEvent.Complete(...)` using `oldPositionMs` and `Track.Duration` (Phase 4 will trust the server's value; for Phase 2 the existing `Track.Duration` lookup via `ITrackRepository` is enough). Opens a fresh event if `newTrackId` is non-null.
  - `ActiveDeviceChangedAsync` closes the open event regardless of `TrackId` continuity (device attribution must remain honest). Opens a fresh event attributed to `newDeviceId` if `oldTrackId` is still the current track and `newDeviceId` is non-null.
  - `SessionClearedAsync` closes any open event for the user. No re-open.
- **Refactor command handlers** under `ColdHarbour.Application/Playback/Commands/` to call the timeline instead of `IPlayEventRepository` directly:
  - `SetQueueCommandHandler.cs`
  - `NextTrackCommandHandler.cs`
  - `PreviousTrackCommandHandler.cs`
  - `AddToQueueCommandHandler.cs`
  - `RemoveFromQueueCommandHandler.cs` (when removing the current item)
  - `TransferPlaybackCommandHandler.cs`
  - `TrackEndedCommandHandler.cs` (closes the event, then asks the timeline to open the next one if `AdvanceAfterEnd` produced a new track)
- **Delete** every direct `IPlayEventRepository` call from these handlers. The only remaining consumers of `IPlayEventRepository` should be the timeline impl, the stats job, and the eventual backfill command in Phase 5.
- **Grep gate:** `git grep -nR "IPlayEventRepository" ColdHarbour.Application/Playback/Commands` must return **zero** matches after this phase.

**Definition of done.**

- Phase 1's track-change, transfer, queue-mutation, and random-walk tests turn green. Pause/resume and duration-trust tests stay red (Phases 3 and 4).
- New unit tests for `PlaySessionTimeline` directly (state-table coverage of all three port methods).
- `dotnet test` ≥ 90 % coverage on `ColdHarbour.Application/Playback`.
- Manual two-device smoke (per `PLAYBACK_MIGRATION.md` workflow): play 5 tracks on Profile A, hit Next/Previous/Transfer freely. After ~30 events, `SELECT COUNT(*) FROM "PlayEvents" WHERE "EndedAt" IS NULL AND "UserId" = ...` returns **0 or 1**.

---

## Phase 3 — Pause-aware listened-time accumulation

> Branch: `playevent-phase-3-pause-aware`.

**Goal.** `PlayEvent` tracks listened-time, not wall-clock-time. A user paused for an hour does not inflate their "minutes listened" stat by an hour.

**Changes.**

- **Domain (`ColdHarbour.Domain/Playback/PlayEvent.cs`):** add fields and methods.
  - `PausedAtUtc: DateTime?` — set when the most recent `PauseListening` was called; null when actively listening.
  - `ListenedMs: long` — accumulator. Incremented on each `ResumeListening` / `Complete` by `(now - PausedAtUtc)`'s complement (i.e. by the listening segment that just ended).
  - `PauseListening(DateTime nowUtc)` — accumulates the active segment into `ListenedMs`, sets `PausedAtUtc = nowUtc`. Idempotent: calling on an already-paused event is a no-op.
  - `ResumeListening(DateTime nowUtc)` — clears `PausedAtUtc`, sets a new segment start. Idempotent: calling on an already-active event is a no-op.
  - `Complete(int positionMs, int durationMs, DateTime nowUtc)` — accumulates the final segment into `ListenedMs` (whether paused or active at the moment of completion), sets `EndedAt = nowUtc`.
  - Invariant: `ListenedMs ≤ (EndedAt - StartedAt).TotalMilliseconds` whenever `EndedAt` is set.
- **Migration `0005_PlayEvent_ListenedMs`:** add `PausedAtUtc timestamptz null`, `ListenedMs bigint not null default 0` columns. Backfill `ListenedMs = EXTRACT(EPOCH FROM (EndedAt - StartedAt)) * 1000` for existing closed rows (best-effort — Phase 5's backfill refines this).
- **Wire pause/resume into the timeline.** Extend `IPlaySessionTimeline`:
  ```csharp
  Task PausedAsync(Guid userId, DateTime nowUtc, CancellationToken ct);
  Task ResumedAsync(Guid userId, DateTime nowUtc, CancellationToken ct);
  ```
  `PauseCommandHandler` / `ResumeCommandHandler` call these. The timeline locates the open event for the user and calls `PauseListening` / `ResumeListening`.
- **`PlaybackStatsJob` (`ColdHarbour.Infrastructure/Jobs/PlaybackStatsJob.cs`):** switch every aggregation that previously read `(EndedAt - StartedAt)` to read `ListenedMs`. Document the change in the job's XML doc-comment.
- **`IPlayEventRepository`** gains a small helper used by the timeline for fast pause/resume:
  ```csharp
  Task<PlayEvent?> FindActiveByUserForMutationAsync(Guid userId, CancellationToken ct);
  ```
  This returns a tracked entity (existing `FindActiveByUserAsync` returns read-only DTOs in some call sites — fix the contract to be unambiguous).

**Definition of done.**

- Phase 1's pause-aware tests turn green: `SetQueue → Pause → Resume` opens exactly one event with two listening segments; `SetQueue → Pause (1 h) → TrackEnded` records `ListenedMs` close to the pre-pause listening window, not the wall-clock hour.
- New domain test matrix on `PlayEvent` covering all four state transitions × idempotency (pause-while-paused, resume-while-active).
- `PlaybackStatsJob` re-run over the same week yields identical aggregates. Asserted by an integration test that runs the job twice and diffs the materialized aggregate rows.
- Migration `0005` applies and rolls back cleanly against an empty Postgres and against the Phase-2 snapshot.

---

## Phase 4 — Server-trusted duration on `trackEnded`

> Branch: `playevent-phase-4-server-duration`.

**Goal.** The server stops trusting the client's claim about how long a track is.

**Changes.**

- **WS message (`/ws/playback` `trackEnded`):** drop `durationMs` from the payload — it is purely informational from now on and not consumed.
  - Hub handler in `ColdHarbour.Api` reads `trackId` + `deviceId` only; logs and discards any `durationMs` the client still sends (graceful for one release; remove in the same PR after the frontend is updated).
- **`TrackEndedCommandHandler`** looks up `Track.Duration` via `ITrackRepository.GetByIdAsync(trackId)` and passes that to `PlayEvent.Complete(positionMs, durationMs: track.Duration, nowUtc)`. The handler **never** reads a duration from the command payload.
- **`TrackEndedCommand` shape:** remove the `DurationMs` property. Update `TrackEndedCommandValidator` (FluentValidation) accordingly. Add a validator rule: `PositionMs >= 0` and `PositionMs <= Track.Duration + 5_000` (5 s tolerance for clock drift / fade-out); rule depends on a `Track` lookup so it runs in the handler, not the validator, if the lookup is too expensive for the pipeline behavior — judgment call documented in the PR.
- **Frontend (`features/player/services/playback-session.service.ts`):** stop sending `durationMs` in `trackEnded`. The audio element's `duration` is informational on the client side only.
- **Stale-event guard remains.** Hub still drops `trackEnded` whose `trackId` doesn't match the current open event's `TrackId` (already in place from `PLAYBACK_MIGRATION.md` Phase 3); this phase doesn't change that.

**Definition of done.**

- Phase 1's duration-trust test turns green: a hand-crafted WS message with `durationMs = 1_800_000` on a 30 s track produces a `PlayEvent` with `Complete(durationMs = Track.Duration)` — not the client value.
- `TrackEndedCommand` DTO has no `DurationMs` field. `git grep -nR "DurationMs" ColdHarbour.Application/Playback/Commands` returns nothing.
- Position-out-of-range validation rejects payloads cleanly with a 400 (REST) / structured WS error (hub).
- Frontend test confirms `trackEnded` payload contains only `trackId` (and `deviceId`).

---

## Phase 5 — Orphan backfill + stats re-runnability

> Branch: `playevent-phase-5-backfill`.

**Goal.** One-shot cleanup of every leaked event accumulated under the pre-Phase-2 code. `PlaybackStatsJob` becomes deterministically re-runnable across any historical window.

**Changes.**

- **New maintenance command `ColdHarbour.Application/Playback/Commands/CloseOrphanedPlayEventsCommand.cs`.** Idempotent. For every `PlayEvent` where `EndedAt IS NULL AND StartedAt < now - 1 day`:
  - **Heuristic close.** Set `EndedAt = LEAST(StartedAt + Track.Duration, StartedAt + 1 day)` — whichever is smaller, on the assumption that no honest listening session lasts longer than the track itself. Set `ListenedMs = max(0, min(Track.Duration, EndedAt - StartedAt))`.
  - Mark the row with a sentinel column `BackfilledAt: DateTime?` (added in this phase's migration) so the heuristic is auditable and a future re-run is a no-op.
- **Migration `0006_PlayEvent_Backfill`:** add `BackfilledAt timestamptz null`. No data change at migration time — the maintenance command does the work.
- **Expose** the command via a one-off `dotnet run --project ColdHarbour.Api -- close-orphans` admin entry point (gated on `Role = Owner`), **not** a public HTTP endpoint. Document the invocation in the PR description.
- **`PlaybackStatsJob` re-runnability guarantee.** After the backfill runs, re-running `PlaybackStatsJob` over the past 90 days twice produces identical aggregate rows. Add an integration test that asserts this.
- **Document the heuristic** in `CLAUDE.md` under "Documented hurdles and solutions" — add an entry explaining why orphaned events exist historically and why they are closed with a track-duration heuristic rather than deleted.

**Definition of done.**

- Running `close-orphans` against a DB populated with Phase 1's random-walk test data (run with the pre-Phase-2 code to deliberately leak events) closes every leaked row. Verified by:
  - `SELECT COUNT(*) FROM "PlayEvents" WHERE "EndedAt" IS NULL AND "StartedAt" < now() - interval '1 day'` → 0.
  - `SELECT COUNT(*) FROM "PlayEvents" WHERE "BackfilledAt" IS NOT NULL` matches the expected leaked count.
- Re-running `close-orphans` immediately is a no-op (zero rows updated).
- `PlaybackStatsJob` integration test asserts identical aggregates across two consecutive runs over the same `(weekStart, weekEnd)` window.
- `CLAUDE.md` gains the heuristic entry and a one-line cross-reference to this migration.

---

## Per-phase checklist (use on every PR)

See `CLAUDE.md` § "Post-implementation checklist" — apply to the feature landed in each phase. The bar is: the phase's *Definition of done* is met, and the checklist passes against the code delivered. Notable items for this migration specifically:

- **Layer discipline.** `PlaySessionTimeline` is an Application service, not Infrastructure. The `IPlaySessionTimeline` port lets handlers stay testable with the in-memory `IPlayEventRepository`.
- **No mock-heavy unit tests.** The invariant tests in Phase 1 are behavior-level against the real handler stack with an in-memory repository — they survive refactors that change which class actually opens an event.
- **DTO ≠ entity.** `TrackEndedCommand` is the DTO; removing `DurationMs` is a deliberate contract change, not an internal refactor — update the frontend in the same PR as Phase 4.
- **WebSocket safe.** Pause/Resume already route through the hub (`PLAYBACK_MIGRATION.md` Phase 2). Phase 3 only adds the timeline side-effect, not a new transport path.
- **Backup-safe.** Migrations `0005` and `0006` must restore cleanly into an empty Postgres (`pg_restore` of last week's backup followed by `dotnet ef database update` succeeds).
- **Active-device guard.** Unchanged from `PLAYBACK_MIGRATION.md` Phase 2 — transport commands stay un-guarded; only `heartbeat` and `stop` carry the guard. This migration does not touch that policy.

---

## Post-migration bridge (not this migration)

Once these five phases land:

- **Per-segment `PlayEvent` rows** — if a future analytic needs per-pause granularity (heatmaps of when users pause inside a track), split `PlayEvent` into a parent + child `PlaySegment` table. The `PlaySessionTimeline` port is the only thing that changes.
- **Skip detection / completion ratio thresholds** — `ListenedMs / Track.Duration` is now honest, so "did the user actually listen to this track" becomes a stable predicate for recommendations.
- **Cross-device listen-time attribution** — `ActiveDeviceChangedAsync` already closes events on transfer; per-device aggregates are a `GROUP BY DeviceId` away in `PlaybackStatsJob`.
- **Real-time listening totals on the user profile** — a denormalized `User.TotalListenedMs` updated on `PlayEvent.Complete` becomes safe to maintain because closes are now centralized.

Each of these is a contained change because the seams already exist after Phase 5.
