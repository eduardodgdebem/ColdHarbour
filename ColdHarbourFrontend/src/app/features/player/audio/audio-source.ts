import { Signal } from '@angular/core';

export interface AudioSource {
  readonly isPlaying: Signal<boolean>;
  readonly currentTime: Signal<number>;
  readonly duration: Signal<number>;
  readonly volume: Signal<number>;
  readonly ended: Signal<boolean>;

  /**
   * Prepare the audio element for `src`. Idempotent for the same src. Does NOT
   * auto-play — callers must invoke `play()` explicitly. This separation lets
   * the playback session service (which owns "is this device active") decide
   * when audio actually starts, instead of letting any track-pick auto-play
   * on every device.
   */
  loadMusic(src: string): void;
  play(): void;
  pause(): void;
  playToggle(): void;
  seekTo(time: number): void;
  setVolume(volume: number): void;
  cleanup(): void;
}
