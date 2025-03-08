import { DestroyRef, effect, Injectable, signal } from '@angular/core';

@Injectable({
  providedIn: 'root'
})
export class AudioService {
  public readonly isPlaying = signal(false);
  public readonly currentTime = signal(0);
  public readonly duration = signal(0);
  public readonly volume = signal(1);

  private audio?: HTMLAudioElement;
  private updateInterval?: ReturnType<typeof setInterval>;

  constructor(private destroyRef: DestroyRef) {
    this.setupEffects();
    this.setupCleanup();
  }

  public loadMusic(musicRef: string): void {
    const previousVolume = this.audio?.volume ?? 1;
    this.cleanupAudio();
    this.resetState();
    
    this.audio = new Audio(musicRef);
    this.audio.volume = previousVolume;    this.setupAudioListeners();
    this.play();
  }

  public playToggle(): void {
    if (!this.audio) return;
    this.isPlaying() ? this.pause() : this.play();
  }

  public seekTo(time: number): void {
    if (!this.audio) return;
    
    this.audio.currentTime = time;
    this.currentTime.set(time);
  }

  public setVolume(volume: number): void {
    this.volume.set(Math.max(0, Math.min(1, volume)));
  }

  private setupEffects(): void {
    effect(() => {
      if (this.isPlaying()) {
        this.startTimeUpdate();
      } else {
        this.stopTimeUpdate();
      }
    });

    effect(() => {
      const volume = this.volume();
      if (this.audio) {
        this.audio.volume = volume;
      }
    });
  }

  private setupCleanup(): void {
    this.destroyRef.onDestroy(() => {
      this.cleanupAudio();
      this.stopTimeUpdate();
    });
  }

  private setupAudioListeners(): void {
    if (!this.audio) return;

    this.audio.addEventListener('loadeddata', () => {
      if (this.audio) {
        this.duration.set(this.audio.duration);
      }
    });

    this.audio.addEventListener('ended', () => {
      this.isPlaying.set(false);
    });
  }

  private startTimeUpdate(): void {
    this.updateInterval = setInterval(() => {
      if (this.audio) {
        this.currentTime.set(this.audio.currentTime);
      }
    }, 100);
  }

  private stopTimeUpdate(): void {
    if (this.updateInterval) {
      clearInterval(this.updateInterval);
      this.updateInterval = undefined;
    }
  }

  private cleanupAudio(): void {
    if (this.audio) {
      this.audio.pause();
      this.audio.src = '';
      this.audio.remove();
      this.audio = undefined;
    }
  }

  private resetState(): void {
    this.currentTime.set(0);
    this.duration.set(0);
    this.isPlaying.set(false);
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
}
