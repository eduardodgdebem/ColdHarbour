import { Component } from '@angular/core';

import { MusicService } from '../../../player/services/music.service';
import type { Music } from '../../../../core/api/api.service';
import { LibraryService } from '../../library.service';

@Component({
  selector: 'app-music-list',
  standalone: true,
  imports: [],
  templateUrl: './music-list.component.html',
  styleUrl: './music-list.component.scss',
})
export class MusicListComponent {
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
