import { Signal } from '@angular/core';

export interface AudioSource {
  readonly isPlaying: Signal<boolean>;
  readonly currentTime: Signal<number>;
  readonly duration: Signal<number>;
  readonly volume: Signal<number>;
  readonly ended: Signal<boolean>;

  loadMusic(src: string): void;
  playToggle(): void;
  seekTo(time: number): void;
  setVolume(volume: number): void;
  cleanup(): void;
}
