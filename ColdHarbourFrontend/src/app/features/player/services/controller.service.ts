import { inject, Injectable } from '@angular/core';
import { AudioService } from './audio.service';
import { MusicService } from './music.service';
import { PlaybackSessionService } from './playback-session.service';

@Injectable({
  providedIn: 'root',
})
export class ControllerService {
  private audioService: AudioService = inject(AudioService);
  private musicService: MusicService = inject(MusicService);
  private playbackSession: PlaybackSessionService = inject(
    PlaybackSessionService,
  );

  private mediaSession: MediaSession | null = null;

  constructor() {
    if ('mediaSession' in navigator) {
      this.mediaSession = navigator.mediaSession;
    }
    this.setupEffects();
  }

  public setupControllerListeners() {
    window.addEventListener('keydown', (event) => {
      this.handleKey(event);
    });

    this.setupMediaSession();
  }

  private setupEffects() {
    // Track-ended handling moved to PlaybackSessionService in phase 3 — it
    // sends a dedicated `trackEnded` message so the server can honor shuffle
    // and repeat instead of always advancing to the next index.
  }

  private togglePlayPause() {
    // Read server state — local audio is silent on inactive devices, so
    // basing the decision on audioService.isPlaying() would always resume.
    const sess = this.playbackSession.session();
    const playing = sess?.isPlaying ?? this.audioService.isPlaying();
    if (playing) {
      this.playbackSession.pause();
    } else {
      this.playbackSession.resume();
    }
  }

  private handleKey(event: KeyboardEvent) {
    switch (event.key) {
      case 'l':
        this.playbackSession.seek(
          (this.audioService.currentTime() + 10) * 1000,
        );
        break;
      case 'j':
        this.playbackSession.seek(
          (this.audioService.currentTime() - 10) * 1000,
        );
        break;
      case ' ':
      case 'k':
        this.togglePlayPause();
        break;
    }
  }

  private setupMediaSession() {
    if (this.mediaSession) {
      this.mediaSession.setActionHandler('play', () => {
        this.playbackSession.resume();
      });
      this.mediaSession.setActionHandler('pause', () => {
        this.playbackSession.pause();
      });
      this.mediaSession.setActionHandler('nexttrack', () => {
        this.playbackSession.next();
      });
      this.mediaSession.setActionHandler('previoustrack', () => {
        this.playbackSession.previous();
      });
    }
  }
}
