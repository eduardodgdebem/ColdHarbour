import { Component, effect, ElementRef, HostBinding, ViewChild } from '@angular/core';
import { AudioService } from '../../services/audio.service';
import { ColorService } from '../../services/color.service';
import { MusicService } from '../../services/music.service';
import { PlayIconComponent } from '../../../../shared/icons/play-icon.component';
import { PauseIconComponent } from '../../../../shared/icons/pause-icon.component';


type SlidersId = "volume" | "progress";

@Component({
  selector: 'app-player',
  standalone: true,
  imports: [PlayIconComponent, PauseIconComponent],
  templateUrl: './player.component.html',
  styleUrl: './player.component.scss',
})
export class PlayerComponent {
  @HostBinding('style.display') get hostDisplay() {
    return this.musicService.currentMusic() ? 'block' : 'none';
  }

  @ViewChild('volumeInput') volumeInput!: ElementRef<HTMLInputElement>;
  @ViewChild('progressInput') progressInput!: ElementRef<HTMLInputElement>;

  constructor(
    public audioService: AudioService,
    public musicService: MusicService,
    private colorService: ColorService
  ) {
    this.setupEffects();
  }

  private setupEffects() {
    effect(() => {
      const music = this.musicService.currentMusic();
      if (music?.audioRef) {
        this.audioService.isPlaying.set(false);
        this.audioService.loadMusic(music.audioRef);
        this.colorService.extractColor(music.imageRef);
      }
    });

    effect(() => {
      const volume = this.audioService.volume();
      if (this.volumeInput) {
        const volumePercentage = volume * 100;
        this.volumeInput.nativeElement.style.setProperty('--volume', `${volumePercentage}%`);
      }
    });

    effect(() => {
      const duration = this.audioService.duration();
      const currentTime = this.audioService.currentTime();
      if (duration && currentTime) {
        const progressPercentage = (currentTime / duration) * 100;
        this.progressInput.nativeElement.style.setProperty('--progress', `${progressPercentage}%`);
      }
    });
  }

  public mainButtonClick(e: Event) {
    const button = e.target as HTMLButtonElement;
    button.blur();
    if (this.musicService.currentMusic()) {
      this.audioService.playToggle();
    }
  }

  public onInputChange(e: Event) {
    const input = e.target as HTMLInputElement;
    const newTime = parseFloat(input.value);
    this.audioService.seekTo(newTime);
  }

  public onSliderClick(e: MouseEvent) {
    const wrapper = e.currentTarget as HTMLDivElement;
    const rect = wrapper.getBoundingClientRect();
    const ratio = (e.clientX - rect.left) / rect.width;
    const newValue = ratio * this.audioService.duration();
    const input = wrapper.querySelector("input") as HTMLInputElement;
    switch (input.id as SlidersId) {
      case 'progress':
        this.audioService.seekTo(newValue);
        break;
      case 'volume':
        this.audioService.volume.set(newValue / this.audioService.duration());
        break;
    }
  }

  public onVolumeChange(e: Event) {
    const input = e.target as HTMLInputElement;
    const volume = parseFloat(input.value);
    this.audioService.setVolume(volume);
  }

  public formatTime(seconds: number): string {
    if (!seconds || isNaN(seconds)) return '0:00';
    const m = Math.floor(seconds / 60);
    const s = Math.floor(seconds % 60);
    return `${m}:${s.toString().padStart(2, '0')}`;
  }
}
