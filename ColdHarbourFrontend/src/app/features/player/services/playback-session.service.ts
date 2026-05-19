import { DestroyRef, effect, Injectable, signal } from '@angular/core';
import { environment } from '../../../../environments/environment';
import { AuthService } from '../../../core/auth/auth.service';
import { DeviceService } from '../../devices/device.service';
import { AudioService } from './audio.service';
import { MusicService } from './music.service';

export type PlaybackSessionDto = {
  userId: string;
  activeDeviceId: string | null;
  trackId: string | null;
  positionMs: number;
  isPlaying: boolean;
  updatedAt: string;
};

export type DeviceDto = {
  id: string;
  name: string;
  lastSeenAt: string;
};

@Injectable({ providedIn: 'root' })
export class PlaybackSessionService {
  readonly session = signal<PlaybackSessionDto | null>(null);
  readonly devices = signal<DeviceDto[]>([]);

  private ws?: WebSocket;
  private heartbeatTimer?: ReturnType<typeof setInterval>;
  private reconnectTimer?: ReturnType<typeof setTimeout>;
  private lastTrackId: string | null = null;

  constructor(
    private authService: AuthService,
    private audioService: AudioService,
    private musicService: MusicService,
    private deviceService: DeviceService,
    destroyRef: DestroyRef,
  ) {
    effect(() => {
      const token = authService.accessToken();
      if (token) this.connect(token);
      else this.disconnect();
    });

    // Send start when a new track is selected
    effect(() => {
      const music = musicService.currentMusic();
      if (!music || music.trackId === this.lastTrackId) return;
      this.lastTrackId = music.trackId;
      this.send({
        type: 'start',
        deviceId: deviceService.getOrCreateDeviceId(),
        trackId: music.trackId,
      });
    });

    // Mirror pause/resume to the session hub
    effect(() => {
      const isPlaying = audioService.isPlaying();
      if (!musicService.currentMusic()) return;
      if (isPlaying) {
        this.send({ type: 'resume' });
      } else {
        this.send({ type: 'pause', positionMs: Math.floor(audioService.currentTime() * 1000) });
      }
    });

    destroyRef.onDestroy(() => this.disconnect());
  }

  transferPlayback(deviceId: string): void {
    this.send({
      type: 'transfer',
      deviceId,
      positionMs: Math.floor(this.audioService.currentTime() * 1000),
    });
  }

  private connect(token: string): void {
    this.disconnect();
    const ws = new WebSocket(`${environment.wsBase}/ws/playback?access_token=${token}`);

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
    if (this.reconnectTimer) { clearTimeout(this.reconnectTimer); this.reconnectTimer = undefined; }
    if (this.ws) { this.ws.close(1000, 'disconnect'); this.ws = undefined; }
  }

  private startHeartbeat(): void {
    this.heartbeatTimer = setInterval(() => {
      this.send({ type: 'heartbeat', positionMs: Math.floor(this.audioService.currentTime() * 1000) });
    }, 2000);
  }

  private stopHeartbeat(): void {
    if (this.heartbeatTimer) { clearInterval(this.heartbeatTimer); this.heartbeatTimer = undefined; }
  }

  private send(msg: object): void {
    if (this.ws?.readyState === WebSocket.OPEN) {
      this.ws.send(JSON.stringify(msg));
    }
  }
}
