import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Location } from '@angular/common';
import { MusicService } from '../../services/music.service';
import { AudioService } from '../../services/audio.service';
import { BackButtonComponent } from '../../../../shared/ui';
import { PlayIconComponent } from '../../../../shared/icons/play-icon.component';
import { PauseIconComponent } from '../../../../shared/icons/pause-icon.component';

@Component({
  selector: 'app-player-page',
  standalone: true,
  imports: [BackButtonComponent, PlayIconComponent, PauseIconComponent],
  templateUrl: './player-page.component.html',
  styleUrl: './player-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlayerPageComponent {
  protected readonly musicService = inject(MusicService);
  protected readonly audioService = inject(AudioService);
  private readonly location = inject(Location);

  readonly currentMusic = this.musicService.currentMusic;
  readonly isPlaying = this.audioService.isPlaying;

  readonly progressPercent = computed(() => {
    const d = this.audioService.duration();
    const t = this.audioService.currentTime();
    if (!d) return 0;
    return Math.min(100, Math.max(0, (t / d) * 100));
  });

  readonly volumePercent = computed(
    () => Math.round(this.audioService.volume() * 100),
  );

  close(): void {
    this.location.back();
  }

  togglePlay(): void {
    this.audioService.playToggle();
  }

  next(): void {
    this.musicService.nextMusic();
  }

  prev(): void {
    this.musicService.previousMusic();
  }

  onSeek(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.audioService.seekTo(parseFloat(input.value));
  }

  onVolume(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.audioService.setVolume(parseFloat(input.value));
  }

  formatTime(seconds: number): string {
    if (!seconds || isNaN(seconds)) return '0:00';
    const m = Math.floor(seconds / 60);
    const s = Math.floor(seconds % 60);
    return `${m}:${s.toString().padStart(2, '0')}`;
  }
}
