# WebSocket Protocol Hardening Migration — Visible bugs in `PlaybackSessionHub`

> Path from today's playback hub (works for the happy path, but has six concrete close-to-the-metal bugs that real users hit) to a hardened protocol where frames are fully reassembled, close codes are honored, deviceIds are authenticated, every transport command rides MediatR + FluentValidation, and the active-device guard means what it says.
>
> Five phases. Each phase ends with a **working, deployable system** — no phase breaks the app. The hub stays online throughout; every change is additive or a bug fix that strictly tightens the protocol.
>
> **TDD is mandatory** (see `CLAUDE.md` § "Working agreement"). Every hub branch, command handler, validator, and pipeline behavior is preceded by a failing xUnit / Jasmine spec. No "tests after." The 90 % minimum coverage holds throughout.
>
> **Branch + merge workflow.** Each phase happens on its own `ws-hardening-phase-N-<slug>` branch and lands as one PR. Branches are cut **from `main`** after the prior phase merges — never stacked on an in-flight branch. A phase only merges to `main` after its *Definition of done* passes (tests, manual smoke, the `CLAUDE.md` post-implementation checklist).
>
> **This file is the progress tracker.** When a phase completes, flip its row in the Status table to `✅ Done — landed on <branch> (<commit>) <YYYY-MM-DD>` in the same PR that lands the work. If the phase changed any architectural fact, update `CLAUDE.md` in the same PR.

## Status

| Phase | Title | Status |
|---|---|---|
| 1 | Frame reassembly + size cap | ✅ Done — landed on `ws-hardening-phase-1-frame-reassembly` 2026-06-06 |
| 2 | Close-code fidelity for token expiry | ✅ Done — landed on `ws-hardening-phase-2-close-codes` 2026-06-06 |
| 3 | SenderDeviceId validation + active-device guard tightening | ✅ Done — landed on `ws-hardening-phase-3-sender-validation` 2026-06-06 |
| 4 | Pause / Resume / Stop through MediatR | ✅ Done — landed on `ws-hardening-phase-4-pause-resume-mediatr` 2026-06-12 |
| 5 | FluentValidation on hub-dispatched commands | ⬜ Pending |

---

## Reconciliation note (2026-06-06)

The codebase diverged from this doc's original premises after a separate **actor refactor** landed. Reading the doc against current `main`:

- **The hub is now a thin parser; mutations run in a per-user `PlaybackUserActor`.** Inbound frames are parsed into `InboundCommand` records in `PlaybackSessionHub`, then enqueued to `PlaybackUserActor` (a single-reader channel per user). The actor dispatches each command through `IMediator.Send(...)`. Wherever the phases below say "the hub calls `session.X` directly and broadcasts inline," read that as "the actor dispatches via MediatR" — the *Api → Application → Domain* path the doc wants is already largely in place.
- **Phase 1 — done.** `PlaybackSessionHub.ReadFullMessageAsync` reassembles fragments until `EndOfMessage`, capped by `COLDHARBOUR_WS_MAX_FRAME_BYTES` (default 1 MB); over-cap closes with `1009 MessageTooBig`. See `CLAUDE.md` hurdle #18.
- **Phase 2 — done.** `Authenticate(...)` now returns an `AuthResult` (`Ok`/`Invalid`/`Expired`); `HandleAsync` maps `Expired → 4001 token_expired`, anything else → `1008 invalid_token`, via `CloseInfoFor` + a single `CloseWithAsync` helper that every close site routes through. Frontend `playback-session.service.ts`: `4001` refreshes then reconnects with the new token (refresh failure → `/login`), `1008` → `/login` (not recoverable), other non-1000 → reconnect.
- **Phase 3 — done (adapted).** `IDeviceRepository.ExistsForUserAsync(userId, deviceId)` added (single-row PK lookup — the doc's `(UserId, Id)` composite index is unnecessary since `Id` is the PK). `PlaybackUserActor.IsActiveDevice` now fails closed: empty id or no-active-device → `false`. **Deviation from the doc's prescribed mechanism (chosen deliberately):** sender-device validation lives at the **actor chokepoint** (`SenderDeviceToValidate` + an `ExistsForUserAsync` check before dispatch), not a `SenderDeviceValidationBehavior` MediatR pipeline + `IHasSenderDevice` marker. Rationale: the WS hub/actor is the *only* client entry for transport commands, so coverage equals the threat surface; the diverged commands carry the live `PlaybackSession` aggregate and pause/resume use a deliberately-optional device id, which the uniform marker/behavior fit poorly. Unknown/empty sender → log `Warning` ("unknown sender device"), drop the message, do **not** close the socket. Heartbeat + stop stay gated by `IsActiveDevice`; resync (read-only) and setRepeatMode/setShuffle (no device) are exempt.
- **Phase 4 — done.** `PauseCommand` / `ResumeCommand` already dispatched via MediatR; `StopCommand` + `StopCommandHandler` now extracted (clears the session, calls `IPlaySessionTimeline.SessionClearedAsync`, never `ClaimActiveIfNone`). `PlaybackUserActor`'s `StopCmd` case keeps the `IsActiveDevice` guard then dispatches `StopCommand` via `IMediator`. Grep gate against `PlaybackUserActor` (`session\.Pause|Resume|Stop|Clear`) returns zero hits in `ColdHarbour.Api`.
- **Phase 5 — still valid, not done.** No `ColdHarbour.Application/Playback/Validators/` directory exists; no validators are registered for the hub-dispatched commands.

**Pre-existing red, unrelated to this migration:** `ColdHarbour.Api.IntegrationTests/Playback/PlaybackUserActorTests` has 7 failing tests on `main` (revision/ack/tick assertions find `Revision == 0`) — a test-harness DI issue, not a product regression. The "all suites green" Definition of done cannot literally hold until those are fixed separately; each WS-hardening phase should be judged green against its *own* new tests plus the previously-passing suite.

---

## Phase 1 — Frame reassembly + size cap

> Branch: `ws-hardening-phase-1-frame-reassembly`.

**Goal.** A `setQueue` with a few hundred track IDs is no longer silently truncated. Hostile or buggy clients cannot exhaust memory by sending unbounded payloads.

The current `ReceiveLoopAsync` in `ColdHarbourBackend/src/ColdHarbour.Api/Playback/PlaybackSessionHub.cs` allocates `var buffer = new byte[4096]`, issues one `ReceiveAsync`, and parses `buffer[0..result.Count]` regardless of `result.EndOfMessage`. Any frame the browser splits — which the browser will do around the 4–16 KB mark — is delivered chopped. Clients see "I queued my album, only the first 100 tracks made it." The malformed JSON tail is caught one layer up and dropped as a `LogWarning`, so the bug is invisible in monitoring.

**Changes.**

- **Hub (`ColdHarbour.Api/Playback/PlaybackSessionHub.cs`):** rewrite `ReceiveLoopAsync` to accumulate frames into a `PooledArrayBufferWriter<byte>` (or `MemoryStream`) until `result.EndOfMessage`. Parse JSON only once the full message is assembled.
- **Hard cap:** introduce `COLDHARBOUR_WS_MAX_FRAME_BYTES` (config option, default `1_048_576` — 1 MB). If the accumulated buffer exceeds the cap mid-assembly, close the socket with `WebSocketCloseStatus.MessageTooBig` (1009) and abandon the read loop. Log at `Warning` with `userId` + byte count.
- **Empty / zero-byte frames:** unchanged behavior — log + ignore. They are not an error.
- **Configuration plumbing:** add the env var to the api `appsettings.json`, the compose file, and the env table in `CLAUDE.md` § "All environment variables".

**Definition of done.**

- **Automated:** new xUnit integration test in `ColdHarbour.Api.IntegrationTests/Playback/PlaybackSessionHubFrameTests.cs`:
  - sends a `setQueue` with 200 random track IDs (≈ 8 KB), forcing the browser-equivalent split via a small `WebSocket` client `WriteAsync` with `endOfMessage: false` on the first half; asserts the server's broadcast `session.queue.Count == 200`.
  - sends a synthetic 10 MB blob; asserts the socket closes with 1009 and the in-memory session is not mutated.
- **Automated:** unit test covering an exactly-`cap`-byte payload (must succeed) and a `cap + 1` payload (must be rejected). This pins the off-by-one.
- **Manual smoke:** open DevTools, queue a real album with > 150 tracks via the UI, confirm the full list appears in the queue panel and on a second device's `session` broadcast.
- All test suites green; coverage on `PlaybackSessionHub` ≥ 90 %.

---

## Phase 2 — Close-code fidelity for token expiry

> Branch: `ws-hardening-phase-2-close-codes`.

**Goal.** When the access token expires mid-WebSocket, the server emits close code `4001` (as `CLAUDE.md` documents and `playback-session.service.ts` already branches on) instead of a generic `PolicyViolation` (1008). The frontend's existing 4001 refresh-and-reconnect branch stops being dead code and the current 3 s reconnect loop with a stale token goes away.

Today, `Authenticate(...)` returns `null` indiscriminately on both `SecurityTokenInvalidException` and `SecurityTokenExpiredException`. `HandleAsync` then closes with `WebSocketCloseStatus.PolicyViolation`. The frontend treats 1008 as a normal disconnect and reconnects every 3 s — with the same expired token, producing a tight server-rejection loop.

**Changes.**

- **Hub (`ColdHarbour.Api/Playback/PlaybackSessionHub.cs`):**
  - Introduce a small result type `enum AuthenticationResult { Ok, Invalid, Expired }` (or `(ClaimsPrincipal?, AuthFailureReason)` — whichever is cleaner). `Authenticate(...)` returns one of the three; only `Ok` carries a principal.
  - `HandleAsync` close-code mapping:
    - `Invalid` → `WebSocketCloseStatus.PolicyViolation` (1008), description `"invalid_token"`.
    - `Expired` → `(WebSocketCloseStatus)4001`, description `"token_expired"`.
  - The same 4001 code is emitted from the existing mid-connection expiry path (where the JWT bearer middleware surfaces expiry on a subsequent message). Audit every `CloseAsync(...)` call site in the hub and route them through a single helper `CloseWithAsync(socket, reason, description)` so this stays consistent.
- **Frontend (`ColdHarbourFrontend/src/app/features/player/services/playback-session.service.ts`):** the `ws.onclose` handler already branches on `e.code === 4001`; verify the branch refreshes the token via `AuthService.refresh()` *before* reconnecting and **does not** schedule a reconnect on the old token. If a refresh fails (refresh token also dead) the service must route to `/login`, not loop.
- **Docs:** the wording in `CLAUDE.md` § "WebSocket playback hub" already promises 4001; no doc change beyond ticking this phase in the Status table.

**Definition of done.**

- **Automated:** xUnit integration test that opens a WS with a JWT signed with a `notBefore + 1ms` short expiry, waits past expiry, sends a message, and asserts the close frame carries `(int)4001` and description `token_expired`. A second test signs with the wrong key and asserts 1008 + `invalid_token`.
- **Automated:** Jasmine spec in `playback-session.service.spec.ts` covering `onclose({ code: 4001 })` → `AuthService.refresh()` called → reconnect with new token; `onclose({ code: 1008 })` → no refresh, route to `/login`.
- **Manual smoke:** set `COLDHARBOUR_ACCESS_TOKEN_TTL=30s` in `.env` for one run; log in, start playback, leave the tab idle for 60 s. Expectation: exactly one close-with-4001, exactly one refresh, exactly one reconnect, playback continues. No 3 s reconnect loop in DevTools' Network → WS tab.
- All test suites green; coverage unchanged.

---

## Phase 3 — SenderDeviceId validation + active-device guard tightening

> Branch: `ws-hardening-phase-3-sender-validation`.

**Goal.** A client can no longer pump arbitrary UUIDs as `deviceId` and claim itself active. Stale-localStorage and malicious clients both fail closed. `IsActiveDevice` stops returning `true` for missing / unparsable values and for the "no active device yet" case on heartbeats.

Today every hub branch trusts whatever string the JSON carries as `deviceId`. `ClaimActiveIfNone` will happily set `ActiveDeviceId = <attacker-supplied-guid>` for the user. Separately, `IsActiveDevice` returns `true` when the deviceId is missing or unparsable (a comment calls it "backward compat" — there are no other clients) and when `ActiveDeviceId` is null, which lets any unknown device dump `PositionMs` heartbeats into the session.

**Changes.**

- **Application port (`ColdHarbour.Application/Playback/Ports/IDeviceRepository.cs`):** add `Task<bool> ExistsForUserAsync(Guid userId, Guid deviceId, CancellationToken ct)`.
- **Infrastructure (`ColdHarbour.Infrastructure/Playback/DeviceRepository.cs`):** implement against `DbContext.Devices.AnyAsync(...)`. Cache-friendly — a single indexed lookup on `(UserId, Id)`. Index on `Devices (UserId, Id)` already exists from Phase 6 of `MIGRATION.md`; verify in the migration file and add if absent.
- **MediatR pipeline behavior (`ColdHarbour.Application/Playback/Pipeline/SenderDeviceValidationBehavior.cs`):** new behavior that runs on any command implementing a marker interface `IHasSenderDevice { Guid UserId; Guid SenderDeviceId; }`. If the deviceId is `Guid.Empty` or `ExistsForUserAsync` returns false, throw a typed `UnknownSenderDeviceException` (or short-circuit via an `OperationResult.Reject(...)` — pick the pattern already used by other behaviors). Register early in the pipeline, *before* FluentValidation, so format errors are caught first.
- **Hub (`ColdHarbour.Api/Playback/PlaybackSessionHub.cs`):**
  - Tighten `IsActiveDevice`:
    - missing / unparsable `deviceId` → return `false` (drop the "backward compat" branch — there are no other clients).
    - `session.ActiveDeviceId is null` → return `false` for `heartbeat` specifically. Heartbeats only make sense once a device owns playback; before that they have nothing to update.
  - Wrap the `UnknownSenderDeviceException` from MediatR: log at `Warning` with `userId`, `senderDeviceId`, `messageType`; drop the message; do **not** close the socket (a stale tab shouldn't lose its session — it'll re-register the device on next user action). Only `heartbeat` floods are worth rate-limiting; defer that to a later phase.
- **Frontend:** no required change. `DeviceService.register()` already runs on login and seeds the row. If validation rejects a message, surface a one-line console warning in `PlaybackSessionService` so devs notice during dev.

**Definition of done.**

- **Automated:** xUnit unit test on `SenderDeviceValidationBehavior` (existing user / unknown deviceId → rejected; existing user / known deviceId → passes through; `Guid.Empty` → rejected).
- **Automated:** integration test that opens a WS, sends `pause { deviceId: <random-uuid> }`, and asserts the session was *not* mutated (no `ActiveDeviceId` claim, no broadcast).
- **Automated:** unit test on the tightened `IsActiveDevice` covering: `(null, valid)` → false; `(activeA, activeA)` → true; `(activeA, valid-but-different)` → false; `("", anything)` → false; `("not-a-guid", anything)` → false.
- **Manual smoke:** clear `localStorage`, log in (new deviceId registers), confirm playback works. Then in DevTools manually overwrite `localStorage.deviceId` with a random UUID, reconnect WS, attempt `pause` — expect no effect on the active device on the other profile and a server log line `unknown sender device`.
- All test suites green; coverage on `PlaybackSessionHub` + new pipeline behavior ≥ 90 %.

---

## Phase 4 — Pause / Resume / Stop through MediatR

> Branch: `ws-hardening-phase-4-pause-resume-mediatr`.

**Goal.** Every transport command rides the same pipeline (MediatR + FluentValidation + sender-device validation). `pause`, `resume`, `stop` stop mutating the domain aggregate directly from the Api layer. The "phantom active owner" bug — where `ClaimActiveIfNone` is called outside the "track is loaded" guard, leaving an active device on an empty session — is fixed in one place.

Today, `pause`/`resume`/`stop` branches in `PlaybackSessionHub` call `session.Pause(...)`/`session.Resume(...)`/`session.Stop(...)` directly and broadcast inline. This violates the layering rule documented in `CLAUDE.md` § "Design patterns" #5 (Api → Application, not Api → Domain), bypasses the validation pipeline, and is the only place where `ClaimActiveIfNone` runs on an aggregate that may have `TrackId is null`.

**Changes.**

- **Application:**
  - New commands `PauseCommand`, `ResumeCommand`, `StopCommand` in `ColdHarbour.Application/Playback/Commands/`. Each carries `UserId`, `SenderDeviceId` (so the Phase 3 pipeline behavior applies), and broadcasts the updated session via the existing hub broadcaster port.
  - Handlers in `ColdHarbour.Application/Playback/Handlers/`. Each handler is the *only* place that decides whether to `ClaimActiveIfNone` — and only does so when `session.TrackId is not null` (i.e. a track is actually loaded). For `StopCommand`, no claim happens — `stop` should never install an owner on an empty session.
  - Match the existing `NextTrackCommand` / `PreviousTrackCommand` shape exactly so the next contributor doesn't have to think about which transport command goes where.
- **Hub (`ColdHarbour.Api/Playback/PlaybackSessionHub.cs`):** the `pause`/`resume`/`stop` branches collapse to `await _mediator.Send(new PauseCommand(userId, deviceId), ct);` etc. No domain calls remain in the Api layer. The hub becomes a thin protocol-shim mapping JSON message types to MediatR commands.
- **Domain:** no signature changes. The `session.Pause()` / `Resume()` / `Stop()` methods stay where they are; only their *callers* move.
- **Active-device guard restatement:** with all transport commands now uniform, the guard's documented narrowing (`heartbeat` + `stop` only) is enforced in exactly one place — at the start of the relevant handlers (or via a small marker-interface check in the hub before dispatch). Update `CLAUDE.md` § "WebSocket playback hub" only if this changes any of the rules already documented there; otherwise leave the prose alone.

**Definition of done.**

- **Automated:** xUnit tests in `ColdHarbour.Application.Tests/Playback/Handlers/`:
  - `PauseCommandHandler`: pauses a playing session, broadcasts; rejects when active-device guard applies and sender ≠ active (if the rule still applies post-narrowing); does not claim active on a session with `TrackId is null`.
  - `ResumeCommandHandler`: mirror of pause.
  - `StopCommandHandler`: clears `TrackId`, `IsPlaying`, `Queue`, `QueueIndex`; closes the open `PlayEvent` if any; never calls `ClaimActiveIfNone`.
- **Automated:** integration test that sends `stop` to a fresh session (no track loaded) and asserts `session.ActiveDeviceId is null` after — i.e. the phantom-owner bug stays fixed.
- **Automated:** `git grep -n "session\\.Pause\\|session\\.Resume\\|session\\.Stop" ColdHarbourBackend/src/ColdHarbour.Api` returns nothing (no direct domain calls left in Api).
- **Manual smoke:** two-device test. Profile B pauses, resumes, stops Profile A's playback. All three behave identically to today from the user's perspective. Restart api mid-test; durable session (Phase 5 of `PLAYBACK_MIGRATION.md`) rehydrates correctly because Pause/Resume now flow through the same write-through path as everything else.
- All test suites ≥ 90 %.

---

## Phase 5 — FluentValidation on hub-dispatched commands

> Branch: `ws-hardening-phase-5-hub-validation`.

**Goal.** Commands dispatched from the WS hub get the same FluentValidation treatment as HTTP commands. Queue length, position ranges, deviceId format, and reorder indices stop being trusted blindly.

REST commands already pass through a `ValidationBehavior<TRequest, TResponse>` (MediatR pipeline behavior, registered in `ColdHarbour.Application/DependencyInjection.cs`). Hub-dispatched commands flow through the same `IMediator.Send` call, so the behavior *fires* — but no validators are registered for the hub-only commands. This phase closes that gap by writing the validators; no pipeline plumbing is needed.

**Changes.**

- **Application validators (`ColdHarbour.Application/Playback/Validators/`):**
  - `SetQueueCommandValidator`: `TrackIds` not null; `TrackIds.Count <= COLDHARBOUR_WS_MAX_QUEUE_SIZE` (new config, default 1000); no `Guid.Empty` entries; `StartIndex >= 0 && StartIndex < TrackIds.Count` when non-empty.
  - `SeekCommandValidator`: `PositionMs >= 0`. (Upper bound is the current track's duration, but the server already clamps; reject only negative values here.)
  - `NextTrackCommandValidator` / `PreviousTrackCommandValidator`: `SenderDeviceId != Guid.Empty` (the Phase 3 pipeline catches unknown senders; this catches the format error pre-DB).
  - `PauseCommandValidator` / `ResumeCommandValidator` / `StopCommandValidator`: same `SenderDeviceId` shape check.
  - `SetRepeatModeCommandValidator`: `Mode` is a defined enum value.
  - `SetShuffleCommandValidator`: no-op beyond `SenderDeviceId` shape (`Enabled` is a `bool`, can't be invalid).
  - `TrackEndedCommandValidator`: `TrackId != Guid.Empty`; `DurationMs >= 0`.
  - **Queue-mutation validators (only land if the corresponding Phase-4 commands from `PLAYBACK_MIGRATION.md` exist on `main`):** `AddToQueueCommandValidator`, `RemoveFromQueueCommandValidator` (`Index >= 0`), `ReorderQueueCommandValidator` (`From >= 0 && To >= 0`).
- **Configuration plumbing:** add `COLDHARBOUR_WS_MAX_QUEUE_SIZE` to `appsettings.json`, the compose file, and the env table in `CLAUDE.md`.
- **Hub error handling:** `FluentValidation.ValidationException` thrown by the pipeline is caught at the hub message dispatch boundary; log at `Warning` with `userId`, `senderDeviceId`, `messageType`, `errors`; drop the message; do **not** close the socket. Same treatment as `UnknownSenderDeviceException` from Phase 3.
- **Frontend:** no required change. Defensive: `PlaybackSessionService.setQueue(...)` already slices large arrays at the UI level (or should); if it doesn't, add a `Math.min(trackIds.length, MAX_QUEUE)` guard so users get truncation in the UI rather than a silent server reject. Surface a console warning when truncation kicks in.

**Definition of done.**

- **Automated:** unit test per validator in `ColdHarbour.Application.Tests/Playback/Validators/`, covering happy path + every failure branch. Validators are pure; tests run in milliseconds.
- **Automated:** integration test sending a `setQueue` with 5000 trackIds and asserting the message is dropped (logged warning) and the session is unchanged.
- **Automated:** integration test sending a `seek { positionMs: -1 }` and asserting the same.
- **Manual smoke:** queue an album of normal size — works. Use DevTools to send a malformed `setQueue` with `startIndex: -1` over the open WS — server logs a `Warning`, session unchanged, no socket close.
- All test suites ≥ 90 %. Coverage on the validators is effectively 100 % given their size.

---

## Per-phase checklist (use on every PR)

See `CLAUDE.md` § "Post-implementation checklist" — apply to the feature landed in each phase. The bar is: the phase's *Definition of done* is met, and the checklist passes against the code delivered. Notable items for this migration specifically:

- **Active-device guard:** Phase 3 *tightens* it (no more permissive `null`/unparsable returns) and Phase 4 reaffirms its narrowing to `heartbeat` + `stop` only. Call this out in every PR description so the diff doesn't look like a regression against `PLAYBACK_MIGRATION.md` Phase 2.
- **Layer discipline:** Phase 4 explicitly moves `pause`/`resume`/`stop` out of the Api layer. After Phase 4, `git grep -n "session\\.\\(Pause\\|Resume\\|Stop\\)" ColdHarbourBackend/src/ColdHarbour.Api` must return zero hits.
- **No new endpoints / no new origins:** everything rides the existing `/ws/playback` socket. No `proxy.conf.json` or Caddyfile changes.
- **New env vars:** Phase 1 introduces `COLDHARBOUR_WS_MAX_FRAME_BYTES`; Phase 5 introduces `COLDHARBOUR_WS_MAX_QUEUE_SIZE`. Both must land in `.env.example`, `docker-compose.yml`, and `CLAUDE.md` § "All environment variables" in the same PR that introduces them.
- **DTO ≠ entity, validation at the edge, cookie Secure flag** — unchanged from `CLAUDE.md`.

---

## Post-migration bridge (not this migration)

Once these five phases land, the hub is hardened against the visible bugs and the protocol is uniform. Adjacent work that becomes easier afterwards but is **not** in scope here:

- **Backpressure / rate limiting on heartbeats.** A device pumping 100 heartbeats/sec should be slowed, not crashed. Easier once Phase 3's sender validation exists.
- **Command acknowledgements (`command-ack` / `command-rejected`).** Today a rejected message is silently dropped; clients can't tell pause-was-rejected from pause-was-applied-but-broadcast-lost. After Phase 5, every rejection path has a typed reason — wiring it back to the sender is one new outbound message type.
- **Hub concurrency / lifecycle rewrite.** The bigger questions (per-user lock granularity, graceful shutdown, replay of missed broadcasts on reconnect) live in their own migration. This one stays out of those waters.

Each of these is a contained change because the seams will exist after Phase 5.
