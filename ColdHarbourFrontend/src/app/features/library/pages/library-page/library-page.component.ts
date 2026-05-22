import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MusicListComponent } from '../../components/music-list/music-list.component';
import { MusicService } from '../../../player/services/music.service';
import type { Music } from '../../../../core/api/api.service';
import { InputComponent } from '../../../../shared/ui';

type SortColumn = 'name' | 'author' | 'duration';
type SortDirection = 'asc' | 'desc';

@Component({
  selector: 'app-library-page',
  standalone: true,
  imports: [FormsModule, MusicListComponent, InputComponent],
  templateUrl: './library-page.component.html',
  styleUrl: './library-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LibraryPageComponent implements OnInit {
  private readonly musicService = inject(MusicService);

  readonly searchQuery = signal('');
  readonly sortColumn = signal<SortColumn>('name');
  readonly sortDirection = signal<SortDirection>('asc');

  readonly sortColumns: ReadonlyArray<{ key: SortColumn; label: string }> = [
    { key: 'name', label: 'TRACK' },
    { key: 'author', label: 'ARTIST' },
    { key: 'duration', label: 'DURATION' },
  ];

  readonly visibleTracks = computed<Music[]>(() => {
    const all = this.musicService.currentPlayList()?.musics ?? [];
    const q = this.searchQuery().trim().toLowerCase();
    const filtered = q
      ? all.filter(
          (m) =>
            m.name.toLowerCase().includes(q) ||
            m.author.toLowerCase().includes(q),
        )
      : all;
    return this.sortTracks(filtered, this.sortColumn(), this.sortDirection());
  });

  readonly hasAnyTracks = computed(
    () => (this.musicService.currentPlayList()?.musics.length ?? 0) > 0,
  );

  readonly emptyMessage = computed(() => {
    if (this.searchQuery().trim()) return 'NO TRACKS MATCH YOUR SEARCH';
    return 'NO TRACKS IN THE LIBRARY';
  });

  ngOnInit(): void {
    this.musicService.setCurrentPlaylist(1);
  }

  setSort(column: SortColumn): void {
    if (this.sortColumn() === column) {
      this.sortDirection.set(this.sortDirection() === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortColumn.set(column);
      this.sortDirection.set('asc');
    }
  }

  glyphFor(column: SortColumn): string {
    if (this.sortColumn() !== column) return '';
    return this.sortDirection() === 'asc' ? '▲' : '▼';
  }

  private sortTracks(
    tracks: Music[],
    column: SortColumn,
    direction: SortDirection,
  ): Music[] {
    const sorted = [...tracks].sort((a, b) => {
      switch (column) {
        case 'name':
          return a.name.localeCompare(b.name);
        case 'author':
          return a.author.localeCompare(b.author);
        case 'duration':
          return a.durationSeconds - b.durationSeconds;
      }
    });
    return direction === 'asc' ? sorted : sorted.reverse();
  }
}
