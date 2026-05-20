import { Injectable, inject } from '@angular/core';
import { LocalAudioSource } from '../audio/local-audio-source';

@Injectable({ providedIn: 'root' })
export class AudioService {
  private readonly source = inject(LocalAudioSource);

  readonly isPlaying = this.source.isPlaying;
  readonly currentTime = this.source.currentTime;
  readonly duration = this.source.duration;
  readonly volume = this.source.volume;
  readonly ended = this.source.ended;

  loadMusic(src: string): void {
    this.source.loadMusic(src);
  }
  playToggle(): void {
    this.source.playToggle();
  }
  seekTo(time: number): void {
    this.source.seekTo(time);
  }
  setVolume(volume: number): void {
    this.source.setVolume(volume);
  }
  cleanup(): void {
    this.source.cleanup();
  }
}
