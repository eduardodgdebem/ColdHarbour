import { DestroyRef, effect, Injectable, signal } from '@angular/core';
import { AudioSource } from './audio-source';

@Injectable({ providedIn: 'root' })
export class LocalAudioSource implements AudioSource {
  readonly isPlaying = signal(false);
  readonly currentTime = signal(0);
  readonly duration = signal(0);
  readonly volume = signal(1);
  readonly ended = signal(false);

  private audio?: HTMLAudioElement;
  private updateInterval?: ReturnType<typeof setInterval>;

  constructor(private destroyRef: DestroyRef) {
    effect(() => {
      if (this.isPlaying()) {
        this.startTimeUpdate();
      } else {
        this.stopTimeUpdate();
      }
    });

    effect(() => {
      const volume = this.volume();
      if (this.audio) this.audio.volume = volume;
    });

    this.destroyRef.onDestroy(() => this.cleanup());
  }

  loadMusic(src: string): void {
    const previousVolume = this.audio?.volume ?? 1;
    this.cleanup();
    this.currentTime.set(0);
    this.duration.set(0);
    this.isPlaying.set(false);

    this.audio = new Audio(src);
    this.audio.volume = previousVolume;
    this.audio.addEventListener('loadeddata', () => {
      if (this.audio) this.duration.set(this.audio.duration);
    });
    this.audio.addEventListener('ended', () => {
      this.isPlaying.set(false);
      this.ended.set(true);
    });
    this.play();
  }

  playToggle(): void {
    if (!this.audio) return;
    this.isPlaying() ? this.pause() : this.play();
  }

  seekTo(time: number): void {
    if (!this.audio) return;
    this.audio.currentTime = time;
    this.currentTime.set(time);
  }

  setVolume(volume: number): void {
    this.volume.set(Math.max(0, Math.min(1, volume)));
  }

  cleanup(): void {
    this.isPlaying.set(false);
    this.stopTimeUpdate();
    if (this.audio) {
      this.audio.pause();
      this.audio.src = '';
      this.audio.remove();
      this.audio = undefined;
    }
  }

  private play(): void {
    if (!this.audio) return;
    this.isPlaying.set(true);
    this.audio.play();
  }

  private pause(): void {
    if (!this.audio) return;
    this.isPlaying.set(false);
    this.audio.pause();
  }

  private startTimeUpdate(): void {
    this.updateInterval = setInterval(() => {
      if (this.audio) this.currentTime.set(this.audio.currentTime);
    }, 100);
  }

  private stopTimeUpdate(): void {
    if (this.updateInterval) {
      clearInterval(this.updateInterval);
      this.updateInterval = undefined;
    }
  }
}
