import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Location } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MusicService } from '../../services/music.service';
import { AudioService } from '../../services/audio.service';
import { PlaybackSessionService } from '../../services/playback-session.service';
import { PlayIconComponent } from '../../../../shared/icons/play-icon.component';
import { PauseIconComponent } from '../../../../shared/icons/pause-icon.component';

@Component({
  selector: 'app-player-page',
  standalone: true,
  imports: [RouterLink, PlayIconComponent, PauseIconComponent],
  templateUrl: './player-page.component.html',
  styleUrl: './player-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlayerPageComponent {
  protected readonly musicService = inject(MusicService);
  protected readonly audioService = inject(AudioService);
  protected readonly playbackSession = inject(PlaybackSessionService);
  private readonly location = inject(Location);

  readonly currentMusic = this.musicService.currentMusic;
  // Reflects the *server's* play state — falls back to local audio when no
  // session has arrived yet. Lets inactive devices show the right icon and
  // remote-control the active device.
  readonly isPlaying = computed<boolean>(() => {
    const sess = this.playbackSession.session();
    if (sess) return sess.isPlaying;
    return this.audioService.isPlaying();
  });

  // Server-aware position + duration. Active device tracks live <audio>;
  // inactive device tracks server position interpolated between heartbeats,
  // and uses the track metadata duration (audioService.duration() is 0 when
  // audio isn't loaded locally).
  readonly displayedTimeSec = computed<number>(
    () => this.playbackSession.displayedPositionMs() / 1000,
  );
  readonly displayedDurationSec = computed<number>(() => {
    const live = this.audioService.duration();
    if (live > 0) return live;
    return this.musicService.currentMusic()?.durationSeconds ?? 0;
  });

  readonly progressPercent = computed(() => {
    const d = this.displayedDurationSec();
    const t = this.displayedTimeSec();
    if (!d) return 0;
    return Math.min(100, Math.max(0, (t / d) * 100));
  });

  readonly shuffleOn = computed<boolean>(
    () => this.playbackSession.session()?.shuffle ?? false,
  );
  readonly repeatMode = computed<'off' | 'all' | 'one'>(
    () => this.playbackSession.session()?.repeatMode ?? 'off',
  );
  readonly repeatLabel = computed(() => {
    switch (this.repeatMode()) {
      case 'all':
        return 'REP•A';
      case 'one':
        return 'REP•1';
      default:
        return 'REP';
    }
  });

  readonly volumePercent = computed(
    () => Math.round(this.audioService.volume() * 100),
  );

  /** Queue items resolved against the loaded playlist for display.
   *  When a queued track is not in `currentPlayList`, render a minimal
   *  placeholder so the row count still matches the server's queue. */
  readonly queueItems = computed(() => {
    const sess = this.playbackSession.session();
    if (!sess) return [];
    const playlist = this.musicService.currentPlayList();
    const lookup = new Map<string, { name: string; author: string }>();
    if (playlist) {
      for (const m of playlist.musics) {
        lookup.set(m.trackId, { name: m.name, author: m.author });
      }
    }
    return sess.queue.map((trackId, index) => {
      const meta = lookup.get(trackId);
      return {
        index,
        trackId,
        name: meta?.name ?? trackId.slice(0, 8),
        author: meta?.author ?? '—',
        isCurrent: index === sess.queueIndex,
      };
    });
  });

  /** Whether the left column is showing the queue panel instead of album art. */
  readonly showQueue = signal(false);

  toggleQueue(): void {
    this.showQueue.update((v) => !v);
  }

  close(): void {
    this.location.back();
  }

  togglePlay(): void {
    if (this.isPlaying()) {
      this.playbackSession.pause();
    } else {
      this.playbackSession.resume();
    }
  }

  toggleShuffle(): void {
    this.playbackSession.setShuffle(!this.shuffleOn());
  }

  cycleRepeat(): void {
    const order: Array<'off' | 'all' | 'one'> = ['off', 'all', 'one'];
    const idx = order.indexOf(this.repeatMode());
    this.playbackSession.setRepeatMode(order[(idx + 1) % order.length]);
  }

  next(): void {
    this.playbackSession.next();
  }

  prev(): void {
    this.playbackSession.previous();
  }

  onSeek(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.playbackSession.seek(parseFloat(input.value) * 1000);
  }

  onVolume(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.audioService.setVolume(parseFloat(input.value));
  }

  removeFromQueueAt(index: number): void {
    this.playbackSession.removeFromQueue(index);
  }

  moveQueueItemUp(index: number): void {
    if (index <= 0) return;
    this.playbackSession.reorderQueue(index, index - 1);
  }

  moveQueueItemDown(index: number): void {
    const queue = this.playbackSession.session()?.queue ?? [];
    if (index >= queue.length - 1) return;
    this.playbackSession.reorderQueue(index, index + 1);
  }

  clearQueue(): void {
    this.playbackSession.clearQueue();
  }

  formatTime(seconds: number): string {
    if (!seconds || isNaN(seconds)) return '0:00';
    const m = Math.floor(seconds / 60);
    const s = Math.floor(seconds % 60);
    return `${m}:${s.toString().padStart(2, '0')}`;
  }
}
