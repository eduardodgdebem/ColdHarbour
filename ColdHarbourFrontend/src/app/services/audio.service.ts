import { effect, Injectable, signal } from '@angular/core';

@Injectable({
  providedIn: 'root'
})
export class AudioService {
  public isPlaying = signal(false);
  public sliderCurrentTime = signal(0);
  public sliderDuration = signal(0);
  private audio!: HTMLAudioElement;

  constructor() {
    let interval: ReturnType<typeof setInterval> | undefined;
    effect(() => {
      if (this.isPlaying()) {
        interval = setInterval(() => {
          this.sliderCurrentTime.set(this.audio.currentTime);
        }, 0);
      } else {
        clearInterval(interval);
      }
    })
  }

  public loadMusic(musicRef: string) {
    this.audio = new Audio(musicRef);
    this.audio.addEventListener("loadeddata", () => {
      this.sliderDuration.set(this.audio.duration);
    });
  }

  public playToggle() {
    if (this.isPlaying()) {
      this.pause();
    } else {
      this.play();
    }
  }

  public setCurrentTime(currentTime: number) {
    this.audio.currentTime = currentTime;
    this.sliderCurrentTime.set(currentTime);
  }

  private play() {
    this.isPlaying.set(true);
    this.audio.play();
  }

  private pause() {
    this.isPlaying.set(false);
    this.audio.pause();
  }
}
