import { Component, effect, ElementRef, ViewChild } from '@angular/core';
import { AudioService } from '../../services/audio.service';
import { MusicService } from '../../services/music.service';
import { PlayIconComponent } from '../../icons/play-icon/play-icon.component';
import { PauseIconComponent } from '../../icons/play-icon/pause-icon.component';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-player',
  standalone: true,
  imports: [PlayIconComponent, PauseIconComponent, CommonModule],
  templateUrl: './player.component.html',
  styleUrl: './player.component.scss',
})
export class PlayerComponent {
  @ViewChild('volumeInput') volumeInput!: ElementRef<HTMLInputElement>;

  constructor(
    public audioService: AudioService,
    public musicService: MusicService
  ) {
    effect(() => {
      const music = this.musicService.currentMusic();
      if (music?.audioRef) {
        this.audioService.isPlaying.set(false);
        this.audioService.loadMusic(music.audioRef);
      }
    });

    effect(() => {
      const volume = this.audioService.volume();
      if (this.volumeInput) {
        const volumePercentage = volume * 100;
        this.volumeInput.nativeElement.style.setProperty('--volume', `${volumePercentage}%`);
      }
    });
  }

  public mainButtonClick() {
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
    const newTime = ratio * this.audioService.duration();
    this.audioService.seekTo(newTime);
  }

  public onVolumeChange(e: Event) {
    const input = e.target as HTMLInputElement;
    const volume = parseFloat(input.value);
    this.audioService.setVolume(volume);
  }
}
