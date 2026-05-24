import {
  Component,
  effect,
  ElementRef,
  ViewChild,
  inject,
} from '@angular/core';
import { Router } from '@angular/router';
import { AudioService } from '../../services/audio.service';
import { MusicService } from '../../services/music.service';
import { PlaybackSessionService } from '../../services/playback-session.service';
import { PlayIconComponent } from '../../../../shared/icons/play-icon.component';
import { PauseIconComponent } from '../../../../shared/icons/pause-icon.component';

type SlidersId = 'volume' | 'progress';

@Component({
  selector: 'app-player',
  standalone: true,
  imports: [PlayIconComponent, PauseIconComponent],
  templateUrl: './player.component.html',
  styleUrl: './player.component.scss',
})
export class PlayerComponent {
  // Visibility is owned by AppComponent's @if (showMiniPlayer()) — no host
  // binding needed (and a getter-based binding raced with the parent's @if
  // on route close, throwing NG0100).

  @ViewChild('volumeInput') volumeInput!: ElementRef<HTMLInputElement>;
  @ViewChild('progressInput') progressInput!: ElementRef<HTMLInputElement>;

  public imageError: boolean = false;

  private readonly router = inject(Router);

  constructor(
    public audioService: AudioService,
    public musicService: MusicService,
    public playbackSession: PlaybackSessionService,
  ) {
    this.setupEffects();
  }

  expand(): void {
    this.router.navigate(['/player']);
  }

  private setupEffects() {
    // Audio loading + color extraction live in MusicService (singleton),
    // so they keep working while this component is unmounted (e.g. when
    // the /player route is active). Only UI-local effects remain here.

    effect(() => {
      const volume = this.audioService.volume();
      if (this.volumeInput) {
        const volumePercentage = volume * 100;
        this.volumeInput.nativeElement.style.setProperty(
          '--volume',
          `${volumePercentage}%`,
        );
      }
    });

    effect(() => {
      const duration = this.audioService.duration();
      const currentTime = this.audioService.currentTime();
      // Guard: the effect can fire during re-instantiation (e.g. closing
      // the /player route triggers AppComponent to remount this component
      // while audio keeps ticking) before the @ViewChild ref resolves.
      if (duration && currentTime && this.progressInput) {
        const progressPercentage = (currentTime / duration) * 100;
        this.progressInput.nativeElement.style.setProperty(
          '--progress',
          `${progressPercentage}%`,
        );
      }
    });

    effect(() => {
      this.musicService.currentMusic();
      this.imageError = false;
    });
  }

  public mainButtonClick(e: Event) {
    const button = e.target as HTMLButtonElement;
    button.blur();
    if (!this.musicService.currentMusic()) return;
    if (this.audioService.isPlaying()) {
      this.playbackSession.pause();
    } else {
      this.playbackSession.resume();
    }
  }

  public nextClick() {
    this.playbackSession.next();
  }

  public previousClick() {
    this.playbackSession.previous();
  }

  public onInputChange(e: Event) {
    const input = e.target as HTMLInputElement;
    const newTime = parseFloat(input.value);
    this.playbackSession.seek(newTime * 1000);
  }

  public onSliderClick(e: MouseEvent) {
    const wrapper = e.currentTarget as HTMLDivElement;
    const rect = wrapper.getBoundingClientRect();
    const ratio = (e.clientX - rect.left) / rect.width;
    const newValue = ratio * this.audioService.duration();
    const input = wrapper.querySelector('input') as HTMLInputElement;
    switch (input.id as SlidersId) {
      case 'progress':
        this.playbackSession.seek(newValue * 1000);
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
