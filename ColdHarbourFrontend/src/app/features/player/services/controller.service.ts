
import { effect, Injectable } from '@angular/core';
import { AudioService } from './audio.service';
import { MusicService } from './music.service';

@Injectable({
  providedIn: 'root',
})
export class ControllerService {
  private mediaSession: MediaSession | null = null;

  constructor(
    private audioService: AudioService,
    private musicService: MusicService
  ) {
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
    effect(() => {
      if (this.audioService.ended()) {
        this.musicService.nextMusic();
        this.audioService.ended.set(false);
      }
    })
  }

  private handleKey(event: KeyboardEvent) {
    switch (event.key) {
      case 'l':
        this.audioService.seekTo(this.audioService.currentTime() + 10);
        break;
      case 'j':
        this.audioService.seekTo(this.audioService.currentTime() - 10);
        break;
      case ' ':
        this.audioService.playToggle();
        break;
      case 'k':
        this.audioService.playToggle();
        break;
    }
  }

  private setupMediaSession() {
    if (this.mediaSession) {
      this.mediaSession.setActionHandler('play', () => {
        if (!this.audioService.isPlaying()) {
          this.audioService.playToggle();
        }
      });
      this.mediaSession.setActionHandler('pause', () => {
        if (this.audioService.isPlaying()) {
          this.audioService.playToggle();
        }
      });
      this.mediaSession.setActionHandler('nexttrack', () => {
        this.musicService.nextMusic();
      });
      this.mediaSession.setActionHandler('previoustrack', () => {
        this.musicService.previousMusic();
      });
    }
  }
}
