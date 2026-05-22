import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { LibraryPageComponent } from './library-page.component';
import { MusicService } from '../../../player/services/music.service';
import { LibraryService } from '../../library.service';
import type { Music, Playlist } from '../../../../core/api/api.service';

function track(id: number, overrides: Partial<Music> = {}): Music {
  return {
    id,
    trackId: `track-${id}`,
    albumId: `album-${id}`,
    name: `Track ${id}`,
    author: `Artist ${id}`,
    audioRef: `/api/stream/track-${id}`,
    imageRef: `/api/artwork/album-${id}`,
    durationSeconds: 200,
    ...overrides,
  };
}

describe('LibraryPageComponent', () => {
  let fixture: ComponentFixture<LibraryPageComponent>;
  let component: LibraryPageComponent;
  let musicService: jasmine.SpyObj<MusicService>;
  let currentPlayList: ReturnType<typeof signal<Playlist | null>>;
  let isLoading: ReturnType<typeof signal<boolean>>;

  function setUp(playlist: Playlist | null = null, loading = false) {
    currentPlayList = signal<Playlist | null>(playlist);
    isLoading = signal<boolean>(loading);

    musicService = jasmine.createSpyObj(
      'MusicService',
      ['setCurrentPlaylist', 'selectMusic', 'isCurrentMusic'],
      {
        currentPlayList,
        isLoading,
        currentMusic: signal<Music | null>(null),
        error: signal<string | null>(null),
      },
    );

    const librarySpy = jasmine.createSpyObj(
      'LibraryService',
      ['deleteTrack'],
      {
        isUploading: signal(false),
        uploadProgress: signal(0),
        syncDiff: signal(null),
      },
    );

    TestBed.configureTestingModule({
      imports: [LibraryPageComponent],
      providers: [
        provideRouter([]),
        { provide: MusicService, useValue: musicService },
        { provide: LibraryService, useValue: librarySpy },
      ],
    });

    fixture = TestBed.createComponent(LibraryPageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function rowNames(): string[] {
    return fixture.debugElement
      .queryAll(By.css('.track .track-name'))
      .map((el) => el.nativeElement.textContent.trim());
  }

  it('requests the all-tracks playlist on init', () => {
    setUp(null, true);
    expect(musicService.setCurrentPlaylist).toHaveBeenCalledWith(1);
  });

  it('renders the search input and the three sort buttons', () => {
    setUp({ id: 1, name: 'All', imageRef: '', musics: [track(1)] });
    expect(fixture.debugElement.query(By.css('.library-search'))).toBeTruthy();
    const sortButtons = fixture.debugElement.queryAll(By.css('.sort-btn'));
    expect(sortButtons.length).toBe(3);
    const labels = sortButtons.map((b) =>
      b.nativeElement.textContent.trim().toUpperCase(),
    );
    expect(labels[0]).toContain('TRACK');
    expect(labels[1]).toContain('ARTIST');
    expect(labels[2]).toContain('DURATION');
  });

  it('sorts by name ascending by default', () => {
    setUp({
      id: 1, name: 'All', imageRef: '',
      musics: [
        track(1, { name: 'Charlie' }),
        track(2, { name: 'Alpha' }),
        track(3, { name: 'Bravo' }),
      ],
    });
    expect(rowNames()).toEqual(['Alpha', 'Bravo', 'Charlie']);
  });

  it('toggles to descending when the active sort column is clicked again', () => {
    setUp({
      id: 1, name: 'All', imageRef: '',
      musics: [
        track(1, { name: 'Charlie' }),
        track(2, { name: 'Alpha' }),
        track(3, { name: 'Bravo' }),
      ],
    });
    const sortButtons = fixture.debugElement.queryAll(By.css('.sort-btn'));
    sortButtons[0].nativeElement.click();
    fixture.detectChanges();
    expect(rowNames()).toEqual(['Charlie', 'Bravo', 'Alpha']);
  });

  it('switches sort column to artist (asc) when the artist button is clicked', () => {
    setUp({
      id: 1, name: 'All', imageRef: '',
      musics: [
        track(1, { name: 'A', author: 'Zara' }),
        track(2, { name: 'B', author: 'Atlas' }),
        track(3, { name: 'C', author: 'Mira' }),
      ],
    });
    const sortButtons = fixture.debugElement.queryAll(By.css('.sort-btn'));
    sortButtons[1].nativeElement.click();
    fixture.detectChanges();
    expect(rowNames()).toEqual(['B', 'C', 'A']);
  });

  it('filters by track name (case-insensitive)', () => {
    setUp({
      id: 1, name: 'All', imageRef: '',
      musics: [
        track(1, { name: 'Lonely Day' }),
        track(2, { name: 'Bright Side' }),
        track(3, { name: 'Daydream' }),
      ],
    });
    component.searchQuery.set('day');
    fixture.detectChanges();
    expect(rowNames().sort()).toEqual(['Daydream', 'Lonely Day']);
  });

  it('filters by artist name (case-insensitive)', () => {
    setUp({
      id: 1, name: 'All', imageRef: '',
      musics: [
        track(1, { name: 'A', author: 'Radiohead' }),
        track(2, { name: 'B', author: 'Burial' }),
        track(3, { name: 'C', author: 'Aphex Twin' }),
      ],
    });
    component.searchQuery.set('RADIO');
    fixture.detectChanges();
    expect(rowNames()).toEqual(['A']);
  });

  it('shows the no-match empty state when filter matches nothing', () => {
    setUp({
      id: 1, name: 'All', imageRef: '',
      musics: [track(1, { name: 'Anything' })],
    });
    component.searchQuery.set('zzzz-no-match');
    fixture.detectChanges();
    const state = fixture.debugElement.query(By.css('.state-msg'));
    expect(state.nativeElement.textContent.trim()).toContain('NO TRACKS MATCH');
  });

  it('marks the active sort column with a glyph', () => {
    setUp({ id: 1, name: 'All', imageRef: '', musics: [track(1)] });
    const sortButtons = fixture.debugElement.queryAll(By.css('.sort-btn'));
    expect(sortButtons[0].nativeElement.classList).toContain('sort-btn--active');
    expect(sortButtons[1].nativeElement.classList).not.toContain('sort-btn--active');
  });
});
