import {
  DestroyRef,
  computed,
  effect,
  Injectable,
  signal,
  untracked,
} from '@angular/core';
import { environment } from '../../../../environments/environment';
import { AuthService } from '../../../core/auth/auth.service';
import { DeviceService } from '../../devices/device.service';
import { AudioService } from './audio.service';
import { MusicService } from './music.service';

export type RepeatMode = 'off' | 'all' | 'one';

export type PlaybackSessionDto = {
  userId: string;
  activeDeviceId: string | null;
  trackId: string | null;
  positionMs: number;
  isPlaying: boolean;
  queue: string[];
  queueIndex: number;
  repeatMode: RepeatMode;
  shuffle: boolean;
  updatedAt: string;
  revision: number;
};

export type DeviceDto = {
  id: string;
  name: string;
  lastSeenAt: string;
};

// If the server's broadcast position is within this many ms of local audio
// position, do not seek — avoids audible micro-skips on every heartbeat echo.
const DRIFT_TOLERANCE_MS = 1000;

@Injectable({ providedIn: 'root' })
export class PlaybackSessionService {
  readonly session = signal<PlaybackSessionDto | null>(null);
  readonly devices = signal<DeviceDto[]>([]);

  private ws?: WebSocket;
  private heartbeatTimer?: ReturnType<typeof setInterval>;
  private reconnectTimer?: ReturnType<typeof setTimeout>;

  // Phase 4: monotonic revision guard — discard stale state broadcasts.
  private localRevision = 0;

  // Phase 4: outbound command tracking for ack correlation.
  private readonly pendingCommands = new Map<string, { sentAt: number; type: string }>();

  // Track the last trackId we sent setQueue for, to avoid duplicate sends.
  private lastTrackId: string | null = null;

  // Track previous session state to detect transitions.
  private prevActiveDeviceId: string | null = null;
  private prevTrackId: string | null = null;
  private prevIsPlaying = false;

  // True while the active-device effect is mutating MusicService /
  // AudioService in response to a server broadcast. Prevents the setQueue
  // effect from echoing those locally-triggered currentMusic changes back
  // to the server.
  private applyingRemote = false;

  // Inactive-device interpolation of remote playback position. Updates every
  // 250ms between server heartbeats so the UI ticks smoothly instead of
  // jumping in 2-second steps.
  private remotePositionMs = signal(0);
  private remoteTickInterval?: ReturnType<typeof setInterval>;

  /**
   * Server-aware playback position in ms for UI display.
   * - No session yet: live local `<audio>` time (initial-load fallback).
   * - Active device: live local `<audio>` time.
   * - Inactive device: server's last `positionMs` interpolated by wall clock.
   */
  readonly displayedPositionMs = computed<number>(() => {
    const sess = this.session();
    if (!sess) return this.audioService.currentTime() * 1000;
    const myId = this.deviceService.getOrCreateDeviceId();
    if (sess.activeDeviceId === myId) {
      return this.audioService.currentTime() * 1000;
    }
    return this.remotePositionMs();
  });

  constructor(
    private authService: AuthService,
    private audioService: AudioService,
    private musicService: MusicService,
    private deviceService: DeviceService,
    destroyRef: DestroyRef,
  ) {
    // Connect after both token and device registration are ready.
    effect(() => {
      const token = authService.accessToken();
      const registered = deviceService.registered();
      if (token && registered) this.connect(token);
      else if (!token) this.disconnect();
    });

    // Emit setQueue whenever the user picks a new track from a loaded
    // playlist. The hub treats setQueue as "play this": it sets TrackId,
    // IsPlaying=true, claims active if no one owns playback. The active
    // device — whoever that is — will then load + play (see effect below).
    effect(() => {
      const music = musicService.currentMusic();
      if (!music || music.trackId === this.lastTrackId) return;
      if (this.applyingRemote) {
        // currentMusic was just mutated by the active-device effect
        // applying a server broadcast — don't echo back as a new setQueue.
        this.lastTrackId = music.trackId;
        return;
      }
      this.lastTrackId = music.trackId;
      const playlist = untracked(() => musicService.currentPlayList());
      if (!playlist) return;
      const idx = playlist.musics.findIndex(
        (t) => t.trackId === music.trackId,
      );
      if (idx < 0) return;
      this.send({
        type: 'setQueue',
        deviceId: deviceService.getOrCreateDeviceId(),
        trackIds: playlist.musics.map((t) => t.trackId),
        startIndex: idx,
      });
    });

    // Server-state mirror effect.
    //   Step 1 — runs on every device: sync musicService.currentMusic to the
    //   server's trackId so inactive devices still show "now playing".
    //   Step 2 — runs only on the active device: load + play + drift-correct
    //   the local <audio>. Inactive devices ensure their audio is silent.
    // No other code in the app loads audio.
    effect(() => {
      const sess = this.session();
      const playlist = this.musicService.currentPlayList();
      if (!sess) return;

      const myId = this.deviceService.getOrCreateDeviceId();
      const wasActive = this.prevActiveDeviceId === myId;
      const isNowActive = sess.activeDeviceId === myId;

      this.prevActiveDeviceId = sess.activeDeviceId;
      this.prevTrackId = sess.trackId;
      this.prevIsPlaying = sess.isPlaying;

      // ── Playlist bootstrap (all devices) ──────────────────────────────
      // When a session has a track but no playlist is loaded, kick off a load.
      // This runs on BOTH the active device and any inactive device (second tab,
      // page refresh) so the mini-player appears on all clients.
      // Guard with isLoading() to avoid a duplicate fetch if one is already
      // in flight (e.g., the PlaylistPageComponent triggered it first).
      if (sess.trackId && !playlist) {
        if (!untracked(() => this.musicService.isLoading())) {
          this.musicService.setCurrentPlaylist(1);
        }
        return; // wait for the playlist signal to change, then re-enter
      }

      // ── Step 1: sync UI on every device ────────────────────────────────
      if (sess.trackId && playlist) {
        const track = playlist.musics.find((m) => m.trackId === sess.trackId);
        if (track) {
          const currentMusic = untracked(() => this.musicService.currentMusic());
          if (currentMusic?.trackId !== track.trackId) {
            this.lastTrackId = track.trackId;
            this.applyingRemote = true;
            try {
              this.musicService.selectMusic(track);
            } finally {
              this.applyingRemote = false;
            }
          }
        }
      }

      // ── Step 2: manage local audio (active device only) ────────────────
      if (!isNowActive) {
        if (wasActive || untracked(() => this.audioService.isPlaying())) {
          queueMicrotask(() => this.audioService.pause());
        }
        return;
      }

      if (!sess.trackId) {
        queueMicrotask(() => this.audioService.cleanup());
        return;
      }

      const track = playlist?.musics.find((m) => m.trackId === sess.trackId);
      if (!track) return;

      queueMicrotask(() => {
        this.audioService.loadMusic(track.audioRef);

        const localMs = this.audioService.currentTime() * 1000;
        if (Math.abs(localMs - sess.positionMs) > DRIFT_TOLERANCE_MS) {
          this.audioService.seekTo(sess.positionMs / 1000);
        }

        const localPlaying = this.audioService.isPlaying();
        if (sess.isPlaying && !localPlaying) this.audioService.play();
        else if (!sess.isPlaying && localPlaying) this.audioService.pause();
      });
    });

    // The active device tells the server when its <audio> hits 'ended'. The
    // server decides what plays next (shuffle/repeat honored there). Inactive
    // devices ignore their own ended signal — there is no audio playing on
    // them, so any 'ended' would be stale.
    effect(() => {
      if (!audioService.ended()) return;
      const sess = untracked(() => this.session());
      const myId = deviceService.getOrCreateDeviceId();
      if (sess && sess.activeDeviceId === myId && sess.trackId) {
        const durationMs = Math.floor(
          untracked(() => audioService.duration()) * 1000,
        );
        this.trackEnded(sess.trackId, durationMs);
      }
      audioService.ended.set(false);
    });

    // Inactive-device position interpolation: re-baseline whenever the server
    // sends a fresh session, then tick locally while the server says playing.
    effect(() => {
      const sess = this.session();
      if (this.remoteTickInterval) {
        clearInterval(this.remoteTickInterval);
        this.remoteTickInterval = undefined;
      }
      if (!sess) {
        this.remotePositionMs.set(0);
        return;
      }
      const myId = this.deviceService.getOrCreateDeviceId();
      if (sess.activeDeviceId === myId) return; // active path reads audioService

      const baseMs = sess.positionMs;
      const baseAt = Date.now();
      this.remotePositionMs.set(baseMs);
      if (sess.isPlaying) {
        this.remoteTickInterval = setInterval(() => {
          this.remotePositionMs.set(baseMs + (Date.now() - baseAt));
        }, 250);
      }
    });

    destroyRef.onDestroy(() => {
      if (this.remoteTickInterval) clearInterval(this.remoteTickInterval);
      this.disconnect();
    });
  }

  // ── Transport: thin wrappers around the hub. The frontend never mutates
  //    audio directly from user input anymore; it always asks the server.

  next(): void {
    this.send({
      type: 'next',
      deviceId: this.deviceService.getOrCreateDeviceId(),
    });
  }

  previous(): void {
    this.send({
      type: 'previous',
      deviceId: this.deviceService.getOrCreateDeviceId(),
    });
  }

  seek(positionMs: number): void {
    this.send({
      type: 'seek',
      deviceId: this.deviceService.getOrCreateDeviceId(),
      positionMs: Math.max(0, Math.floor(positionMs)),
    });
  }

  pause(): void {
    this.send({
      type: 'pause',
      deviceId: this.deviceService.getOrCreateDeviceId(),
    });
  }

  resume(): void {
    this.send({
      type: 'resume',
      deviceId: this.deviceService.getOrCreateDeviceId(),
    });
  }

  setRepeatMode(mode: RepeatMode): void {
    this.send({ type: 'setRepeatMode', mode });
  }

  setShuffle(enabled: boolean): void {
    this.send({ type: 'setShuffle', enabled });
  }

  /**
   * Called when the active device's `<audio>` element fires the `ended`
   * event. The server then runs AdvanceAfterEnd (honoring repeat + shuffle)
   * and broadcasts the new session.
   */
  trackEnded(trackId: string, durationMs: number): void {
    this.send({
      type: 'trackEnded',
      deviceId: this.deviceService.getOrCreateDeviceId(),
      trackId,
      durationMs: Math.max(0, Math.floor(durationMs)),
    });
  }

  /** Append (or insert at `position`) — server claims sender as active if no
   *  device owns playback, and primes playback when the queue was empty. */
  addToQueue(trackId: string, position?: number): void {
    const msg: Record<string, unknown> = {
      type: 'addToQueue',
      deviceId: this.deviceService.getOrCreateDeviceId(),
      trackId,
    };
    if (position !== undefined) msg['position'] = position;
    this.send(msg as Record<string, unknown>);
  }

  removeFromQueue(index: number): void {
    this.send({
      type: 'removeFromQueue',
      deviceId: this.deviceService.getOrCreateDeviceId(),
      index,
    });
  }

  reorderQueue(from: number, to: number): void {
    this.send({
      type: 'reorderQueue',
      deviceId: this.deviceService.getOrCreateDeviceId(),
      from,
      to,
    });
  }

  clearQueue(): void {
    this.send({
      type: 'clearQueue',
      deviceId: this.deviceService.getOrCreateDeviceId(),
    });
  }

  transferPlayback(deviceId: string): void {
    const myId = this.deviceService.getOrCreateDeviceId();
    const sess = this.session();
    // If this device is currently active, use local audio time (more accurate
    // than the last heartbeat). Otherwise use the server session's position.
    const positionMs =
      sess?.activeDeviceId === myId
        ? Math.floor(this.audioService.currentTime() * 1000)
        : (sess?.positionMs ?? 0);

    this.send({ type: 'transfer', deviceId, positionMs });
  }

  private connect(token: string): void {
    this.disconnect();
    const ws = new WebSocket(
      `${environment.wsBase}/ws/playback?access_token=${token}`,
    );

    ws.onopen = () => this.startHeartbeat();

    ws.onmessage = (e) => {
      const msg = JSON.parse(e.data as string);
      if (msg.type === 'state') {
        const incoming: PlaybackSessionDto = msg.session;
        const rev: number | undefined = incoming.revision;
        if (rev == null || rev > this.localRevision) {
          if (rev != null) this.localRevision = rev;
          this.session.set(incoming);
        }
      }
      if (msg.type === 'tick') {
        const tick = msg as { positionMs: number; isPlaying: boolean; revision: number; trackId?: string | null };
        if (tick.revision > this.localRevision) {
          // Session is behind — request a full resync and do not apply drift
          // correction based on stale revision context.
          this.send({ type: 'resync', lastSeenRevision: this.localRevision, deviceId: this.deviceService.getOrCreateDeviceId() });
          return;
        }
        // Do NOT write to session() here. Writing the tick's positionMs into the
        // session signal re-triggers the full session effect on every 2-second
        // heartbeat, causing unintended loadMusic / seek calls on material-change logic.
        const myId = this.deviceService.getOrCreateDeviceId();
        const currentSess = this.session();
        // Drop stale ticks: a heartbeat for the previous track can arrive after
        // "next"/"transfer" sets the session to a new track. Its positionMs (e.g.
        // 30 000 ms from the old track) would wrongly seek the new track.
        if (tick.trackId !== undefined && tick.trackId !== currentSess?.trackId) return;
        if (currentSess?.activeDeviceId === myId) {
          const localMs = this.audioService.currentTime() * 1000;
          if (Math.abs(localMs - tick.positionMs) > DRIFT_TOLERANCE_MS) {
            this.audioService.seekTo(tick.positionMs / 1000);
          }
          const localPlaying = this.audioService.isPlaying();
          if (tick.isPlaying && !localPlaying) this.audioService.play();
          else if (!tick.isPlaying && localPlaying) this.audioService.pause();
        }
      }
      if (msg.type === 'devices') this.devices.set(msg.devices);
      if (msg.type === 'command-ack') {
        this.pendingCommands.delete(msg.commandId as string);
      }
    };

    ws.onclose = (e) => {
      this.stopHeartbeat();
      if (e.code === 4001) {
        this.authService.refresh().subscribe({
          next: (t) => this.connect(t),
          error: () => {},
        });
      } else if (e.code !== 1000) {
        this.reconnectTimer = setTimeout(() => {
          const t = this.authService.accessToken();
          if (t) this.connect(t);
        }, 3000);
      }
    };

    this.ws = ws;
  }

  private disconnect(): void {
    this.stopHeartbeat();
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = undefined;
    }
    if (this.ws) {
      this.ws.close(1000, 'disconnect');
      this.ws = undefined;
    }
  }

  private startHeartbeat(): void {
    this.heartbeatTimer = setInterval(() => {
      const myId = this.deviceService.getOrCreateDeviceId();
      const sess = this.session();
      // Only the active device sends heartbeats — the server's guard would
      // drop ours anyway, and sending stale 0s from an inactive device just
      // pollutes the wire.
      if (!sess || sess.activeDeviceId !== myId) return;
      this.send({
        type: 'heartbeat',
        deviceId: myId,
        positionMs: Math.floor(this.audioService.currentTime() * 1000),
      });
    }, 2000);
  }

  private stopHeartbeat(): void {
    if (this.heartbeatTimer) {
      clearInterval(this.heartbeatTimer);
      this.heartbeatTimer = undefined;
    }
  }

  private send(msg: Record<string, unknown>): void {
    if (this.ws?.readyState === WebSocket.OPEN) {
      const commandId = this.generateUUID();
      const payload = { ...msg, commandId };
      this.pendingCommands.set(commandId, {
        sentAt: Date.now(),
        type: String(msg['type'] ?? 'unknown'),
      });
      this.ws.send(JSON.stringify(payload));
    }
  }

  private generateUUID(): string {
    if (typeof crypto !== 'undefined' && crypto.randomUUID) {
      return crypto.randomUUID();
    }
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
      const r = (Math.random() * 16) | 0;
      return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16);
    });
  }
}
