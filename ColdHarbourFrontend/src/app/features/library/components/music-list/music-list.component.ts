import { Component, computed, input } from '@angular/core';

import { MusicService } from '../../../player/services/music.service';
import type { Music } from '../../../../core/api/api.service';
import { LibraryService } from '../../library.service';
import { BadgeComponent } from '../../../../shared/ui';

@Component({
  selector: 'app-music-list',
  standalone: true,
  imports: [BadgeComponent],
  templateUrl: './music-list.component.html',
  styleUrl: './music-list.component.scss',
})
export class MusicListComponent {
  /** Optional override. When provided, the list renders these tracks instead of musicService.currentPlayList(). */
  readonly musics = input<Music[] | null>(null);

  /** Optional empty-state message override (replaces the default "no tracks" copy). */
  readonly emptyMessage = input<string | null>(null);

  public imageErrorById = new Map<number, boolean>();

  protected readonly tracks = computed<Music[]>(() => {
    const override = this.musics();
    if (override !== null) return override;
    return this.musicService.currentPlayList()?.musics ?? [];
  });

  constructor(
    public musicService: MusicService,
    public libraryService: LibraryService,
  ) {}

  selectMusic(music: Music) {
    this.musicService.selectMusic(music);
  }

  isCurrentMusic(music: Music): boolean {
    return this.musicService.isCurrentMusic(music);
  }

  deleteTrack(event: Event, trackId: string) {
    event.stopPropagation();
    if (confirm('Delete this track from your library?')) {
      this.libraryService.deleteTrack(trackId);
    }
  }

  formatDuration(seconds: number): string {
    const m = Math.floor(seconds / 60);
    const s = Math.floor(seconds % 60);
    return `${m}:${s.toString().padStart(2, '0')}`;
  }
}
