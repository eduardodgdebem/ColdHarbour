import {
  Component,
  computed,
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

  // Reflects the *server's* play state — falls back to local audio when no
  // session has arrived yet. Lets inactive devices show the right icon and
  // remote-control the active device.
  readonly effectivePlaying = computed<boolean>(() => {
    const sess = this.playbackSession.session();
    if (sess) return sess.isPlaying;
    return this.audioService.isPlaying();
  });

  // Server-aware position + duration. Active device reads its <audio> live;
  // inactive devices interpolate the server's last positionMs and read the
  // track's metadata-provided duration (audioService.duration() is 0 on a
  // device that hasn't loaded the audio).
  readonly displayedTimeSec = computed<number>(
    () => this.playbackSession.displayedPositionMs() / 1000,
  );
  readonly displayedDurationSec = computed<number>(() => {
    const live = this.audioService.duration();
    if (live > 0) return live;
    return this.musicService.currentMusic()?.durationSeconds ?? 0;
  });

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
      const duration = this.displayedDurationSec();
      const currentTime = this.displayedTimeSec();
      // Guard: the effect can fire during re-instantiation (e.g. closing
      // the /player route triggers AppComponent to remount this component
      // while audio keeps ticking) before the @ViewChild ref resolves.
      if (duration && this.progressInput) {
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
    const target = (e.currentTarget ?? e.target) as HTMLElement | null;
    target?.blur?.();
    if (!this.musicService.currentMusic()) return;
    if (this.effectivePlaying()) {
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
    const input = wrapper.querySelector('input') as HTMLInputElement;
    switch (input.id as SlidersId) {
      case 'progress': {
        const newSec = ratio * this.displayedDurationSec();
        this.playbackSession.seek(newSec * 1000);
        break;
      }
      case 'volume':
        this.audioService.volume.set(ratio);
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
