import {
  DestroyRef,
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
import type { Music } from '../../../core/api/api.service';

export type PlaybackSessionDto = {
  userId: string;
  activeDeviceId: string | null;
  trackId: string | null;
  positionMs: number;
  isPlaying: boolean;
  queue: string[];
  queueIndex: number;
  updatedAt: string;
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

  // Track the last trackId we sent setQueue for, to avoid duplicate sends.
  private lastTrackId: string | null = null;

  // Track previous session values to detect meaningful transitions.
  private prevActiveDeviceId: string | null = null;
  private prevTrackId: string | null = null;
  private prevIsPlaying = false;

  // Pending activation: set when we become the active device but the
  // playlist isn't loaded yet.
  private pendingActivation: { trackId: string; positionMs: number } | null =
    null;

  constructor(
    private authService: AuthService,
    private audioService: AudioService,
    private musicService: MusicService,
    private deviceService: DeviceService,
    destroyRef: DestroyRef,
  ) {
    // Connect only after both token and device registration are ready.
    // Device registration must complete first so the hub's initial
    // BroadcastDevicesAsync finds the row; otherwise the devices list
    // arrives empty.
    effect(() => {
      const token = authService.accessToken();
      const registered = deviceService.registered();
      if (token && registered) this.connect(token);
      else if (!token) this.disconnect();
    });

    // Emit setQueue whenever the user picks a new track from a loaded playlist.
    // Phase 2: setQueue is the single entry into "play this." The legacy 'start'
    // message has been retired — setQueue now carries the start semantics
    // (sets TrackId, IsPlaying=true, sender-claims-active) on the server.
    effect(() => {
      const music = musicService.currentMusic();
      if (!music || music.trackId === this.lastTrackId) return;
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

    // React to server-pushed session changes: transfers, remote next/prev/seek,
    // remote pause/resume. The active device drives its own <audio>; inactive
    // devices stay silent (and pause if they lose active status).
    // Also reads currentPlayList so the effect re-runs when the playlist loads.
    effect(() => {
      const sess = this.session();
      const playlist = this.musicService.currentPlayList();

      if (!sess) return;

      const myId = this.deviceService.getOrCreateDeviceId();
      const wasActive = this.prevActiveDeviceId === myId;
      const isNowActive = sess.activeDeviceId === myId;
      const prevTrackId = this.prevTrackId;
      const prevIsPlaying = this.prevIsPlaying;

      this.prevActiveDeviceId = sess.activeDeviceId;
      this.prevTrackId = sess.trackId;
      this.prevIsPlaying = sess.isPlaying;

      if (
        isNowActive &&
        (!wasActive || (sess.trackId !== null && sess.trackId !== prevTrackId))
      ) {
        // Just became the active device, or the track changed while we're active.
        if (sess.trackId) {
          this.pendingActivation = {
            trackId: sess.trackId,
            positionMs: sess.positionMs,
          };
        }
      } else if (wasActive && !isNowActive) {
        // Lost active status — pause local audio without echoing to the server.
        this.pendingActivation = null;
        queueMicrotask(() => this.pauseLocally());
        return;
      } else if (isNowActive && sess.trackId === prevTrackId) {
        // Same track, still active — react to remote pause/resume + drift seek.
        if (sess.isPlaying !== prevIsPlaying) {
          queueMicrotask(() => this.matchIsPlayingLocally(sess.isPlaying));
        }
        const localMs = untracked(() => this.audioService.currentTime() * 1000);
        if (Math.abs(localMs - sess.positionMs) > DRIFT_TOLERANCE_MS) {
          queueMicrotask(() =>
            this.audioService.seekTo(sess.positionMs / 1000),
          );
        }
      }

      // Apply a pending activation now if the playlist is ready.
      if (this.pendingActivation && playlist) {
        const { trackId, positionMs } = this.pendingActivation;
        const track = playlist.musics.find((m) => m.trackId === trackId);
        if (track) {
          this.pendingActivation = null;
          queueMicrotask(() => this.applyActivation(track, positionMs));
        } else if (!untracked(() => this.musicService.isLoading())) {
          this.musicService.setCurrentPlaylist(1);
        }
      } else if (
        this.pendingActivation &&
        !playlist &&
        !untracked(() => this.musicService.isLoading())
      ) {
        this.musicService.setCurrentPlaylist(1);
      }
    });

    destroyRef.onDestroy(() => this.disconnect());
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

  transferPlayback(deviceId: string): void {
    const myId = this.deviceService.getOrCreateDeviceId();
    const sess = this.session();
    // If this device is currently active, use local audio time (more accurate
    // than last heartbeat). Otherwise use the server session's position
    // (the active device's last reported position).
    const positionMs =
      sess?.activeDeviceId === myId
        ? Math.floor(this.audioService.currentTime() * 1000)
        : (sess?.positionMs ?? 0);

    this.send({ type: 'transfer', deviceId, positionMs });
  }

  private applyActivation(track: Music, positionMs: number): void {
    const currentMusic = untracked(() => this.musicService.currentMusic());

    if (currentMusic?.trackId === track.trackId) {
      // Same track — just seek to the transferred position and play if paused.
      if (!untracked(() => this.audioService.isPlaying())) {
        this.audioService.seekTo(positionMs / 1000);
        this.audioService.playToggle();
      }
      return;
    }

    // Different track — load it directly (works even if the player component
    // isn't mounted). lastTrackId guards the setQueue effect from re-emitting
    // for a track the server already knows about.
    this.lastTrackId = track.trackId;
    this.musicService.selectMusic(track);
    this.audioService.loadMusic(track.audioRef);
    if (positionMs > 0) {
      setTimeout(() => this.audioService.seekTo(positionMs / 1000), 150);
    }
  }

  private pauseLocally(): void {
    if (!untracked(() => this.audioService.isPlaying())) return;
    this.audioService.playToggle();
  }

  private matchIsPlayingLocally(shouldBePlaying: boolean): void {
    const isPlaying = untracked(() => this.audioService.isPlaying());
    if (isPlaying === shouldBePlaying) return;
    this.audioService.playToggle();
  }

  private connect(token: string): void {
    this.disconnect();
    const ws = new WebSocket(
      `${environment.wsBase}/ws/playback?access_token=${token}`,
    );

    ws.onopen = () => this.startHeartbeat();

    ws.onmessage = (e) => {
      const msg = JSON.parse(e.data as string);
      if (msg.type === 'session') this.session.set(msg.session);
      if (msg.type === 'devices') this.devices.set(msg.devices);
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
      this.send({
        type: 'heartbeat',
        deviceId: this.deviceService.getOrCreateDeviceId(),
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

  private send(msg: object): void {
    if (this.ws?.readyState === WebSocket.OPEN) {
      this.ws.send(JSON.stringify(msg));
    }
  }
}
