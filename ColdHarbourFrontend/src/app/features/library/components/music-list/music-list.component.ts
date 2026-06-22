import { Component, computed, input, output, signal } from '@angular/core';

import { MusicService } from '../../../player/services/music.service';
import { PlaybackSessionService } from '../../../player/services/playback-session.service';
import type { Music } from '../../../../core/api/api.service';
import { LibraryService } from '../../library.service';
import {
  BadgeComponent,
  ButtonComponent,
  ModalComponent,
} from '../../../../shared/ui';

@Component({
  selector: 'app-music-list',
  standalone: true,
  imports: [BadgeComponent, ButtonComponent, ModalComponent],
  templateUrl: './music-list.component.html',
  styleUrl: './music-list.component.scss',
})
export class MusicListComponent {
  /** Optional override. When provided, the list renders these tracks instead of musicService.currentPlayList(). */
  readonly musics = input<Music[] | null>(null);

  /** Optional empty-state message override (replaces the default "no tracks" copy). */
  readonly emptyMessage = input<string | null>(null);

  /** When true, each row shows an edit affordance that emits `(edit)`. */
  readonly editable = input(false);

  /** Emitted when the row's edit button is clicked (only when `editable`). */
  readonly edit = output<Music>();

  public imageErrorById = new Map<number, boolean>();

  readonly deleteCandidate = signal<Music | null>(null);

  protected readonly tracks = computed<Music[]>(() => {
    const override = this.musics();
    if (override !== null) return override;
    return this.musicService.currentPlayList()?.musics ?? [];
  });

  constructor(
    public musicService: MusicService,
    public libraryService: LibraryService,
    public playbackSession: PlaybackSessionService,
  ) {}

  selectMusic(music: Music) {
    // Server-authoritative: declare the queue (the list this component is
    // actually showing) + the picked index. The server echoes a broadcast and
    // the session effect writes currentMusic — we must not set it here.
    const list = this.tracks();
    const idx = list.findIndex((m) => m.trackId === music.trackId);
    if (idx < 0) return;
    this.playbackSession.setQueue(
      list.map((m) => m.trackId),
      idx,
    );
  }

  addToQueue(event: Event, music: Music) {
    event.stopPropagation();
    this.playbackSession.addToQueue(music.trackId);
  }

  requestEdit(event: Event, music: Music) {
    event.stopPropagation();
    this.edit.emit(music);
  }

  isCurrentMusic(music: Music): boolean {
    return this.musicService.isCurrentMusic(music);
  }

  requestDelete(event: Event, music: Music) {
    event.stopPropagation();
    this.deleteCandidate.set(music);
  }

  confirmDelete() {
    const target = this.deleteCandidate();
    if (target) {
      this.libraryService.deleteTrack(target.trackId);
    }
    this.deleteCandidate.set(null);
  }

  cancelDelete() {
    this.deleteCandidate.set(null);
  }

  formatDuration(seconds: number): string {
    const m = Math.floor(seconds / 60);
    const s = Math.floor(seconds % 60);
    return `${m}:${s.toString().padStart(2, '0')}`;
  }
}
