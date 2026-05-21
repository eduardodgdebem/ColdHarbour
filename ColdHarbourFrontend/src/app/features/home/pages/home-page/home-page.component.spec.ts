import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { HomePageComponent } from './home-page.component';
import { MusicService } from '../../../player/services/music.service';
import { AuthService } from '../../../../core/auth/auth.service';
import type { Music, Playlist } from '../../../../core/api/api.service';

function track(
  id: number,
  overrides: Partial<Music> = {},
): Music {
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

describe('HomePageComponent', () => {
  let fixture: ComponentFixture<HomePageComponent>;
  let component: HomePageComponent;
  let musicService: jasmine.SpyObj<MusicService>;
  let currentPlayList: ReturnType<typeof signal<Playlist | null>>;
  let isLoading: ReturnType<typeof signal<boolean>>;
  let email: ReturnType<typeof signal<string | null>>;

  function setUp(opts: {
    playlist?: Playlist | null;
    loading?: boolean;
    email?: string | null;
  } = {}) {
    currentPlayList = signal<Playlist | null>(opts.playlist ?? null);
    isLoading = signal<boolean>(opts.loading ?? false);
    email = signal<string | null>(opts.email ?? null);

    musicService = jasmine.createSpyObj(
      'MusicService',
      ['setCurrentPlaylist', 'selectMusic'],
      {
        currentPlayList,
        isLoading,
      },
    );

    TestBed.configureTestingModule({
      imports: [HomePageComponent],
      providers: [
        provideRouter([]),
        { provide: MusicService, useValue: musicService },
        { provide: AuthService, useValue: { email } },
      ],
    });

    fixture = TestBed.createComponent(HomePageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('requests the all-tracks playlist on init', () => {
    setUp({ loading: true });
    expect(musicService.setCurrentPlaylist).toHaveBeenCalledWith(1);
  });

  it('renders the loading state while isLoading is true', () => {
    setUp({ loading: true });
    expect(fixture.debugElement.query(By.css('.status'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('.harbour'))).toBeNull();
    expect(fixture.debugElement.query(By.css('.dry-dock'))).toBeNull();
  });

  it('renders the DRY DOCK empty state when not loading and the playlist has zero tracks', () => {
    setUp({
      loading: false,
      playlist: { id: 1, name: 'All', imageRef: '', musics: [] },
    });
    expect(fixture.debugElement.query(By.css('.dry-dock'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('.harbour'))).toBeNull();
  });

  it('renders the dashboard when the playlist has tracks', () => {
    setUp({
      loading: false,
      playlist: {
        id: 1,
        name: 'All',
        imageRef: '',
        musics: [track(1), track(2)],
      },
    });
    expect(fixture.debugElement.query(By.css('.harbour'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('.manifest'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('.arrivals'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('.control-room'))).toBeTruthy();
  });

  it('displays the track count from the playlist', () => {
    setUp({
      loading: false,
      playlist: {
        id: 1,
        name: 'All',
        imageRef: '',
        musics: [track(1), track(2), track(3)],
      },
    });
    const values = fixture.debugElement.queryAll(By.css('.stat__value'));
    expect(values[0].nativeElement.textContent.trim()).toBe('3');
  });

  it('formats total duration as Xh YYm when over an hour', () => {
    setUp({
      loading: false,
      playlist: {
        id: 1,
        name: 'All',
        imageRef: '',
        musics: [
          track(1, { durationSeconds: 3600 }),
          track(2, { durationSeconds: 1800 }),
        ],
      },
    });
    const values = fixture.debugElement.queryAll(By.css('.stat__value'));
    expect(values[1].nativeElement.textContent.trim()).toBe('1H 30M');
  });

  it('formats total duration as YYm when under an hour', () => {
    setUp({
      loading: false,
      playlist: {
        id: 1,
        name: 'All',
        imageRef: '',
        musics: [track(1, { durationSeconds: 1500 })],
      },
    });
    const values = fixture.debugElement.queryAll(By.css('.stat__value'));
    expect(values[1].nativeElement.textContent.trim()).toBe('25M');
  });

  it('counts unique albums for the albumCount stat', () => {
    setUp({
      loading: false,
      playlist: {
        id: 1,
        name: 'All',
        imageRef: '',
        musics: [
          track(1, { albumId: 'a' }),
          track(2, { albumId: 'a' }),
          track(3, { albumId: 'b' }),
        ],
      },
    });
    const values = fixture.debugElement.queryAll(By.css('.stat__value'));
    expect(values[2].nativeElement.textContent.trim()).toBe('2');
  });

  it('shows at most 8 items in the recently-added rail', () => {
    const many = Array.from({ length: 12 }, (_, i) => track(i + 1));
    setUp({
      loading: false,
      playlist: { id: 1, name: 'All', imageRef: '', musics: many },
    });
    const tiles = fixture.debugElement.queryAll(By.css('.arrival'));
    expect(tiles.length).toBe(8);
  });

  it('shows the most-recently-added track first in the rail', () => {
    const t1 = track(1, { name: 'Older' });
    const t2 = track(2, { name: 'Newer' });
    setUp({
      loading: false,
      playlist: { id: 1, name: 'All', imageRef: '', musics: [t1, t2] },
    });
    const names = fixture.debugElement
      .queryAll(By.css('.arrival__name'))
      .map((el) => el.nativeElement.textContent.trim());
    expect(names).toEqual(['Newer', 'Older']);
  });

  it('calls musicService.selectMusic when an arrival tile is clicked', () => {
    const t1 = track(1);
    const t2 = track(2);
    setUp({
      loading: false,
      playlist: { id: 1, name: 'All', imageRef: '', musics: [t1, t2] },
    });
    const tiles = fixture.debugElement.queryAll(By.css('.arrival'));
    tiles[0].nativeElement.click();
    expect(musicService.selectMusic).toHaveBeenCalledWith(t2);
  });

  it('derives userName from the email local-part and uppercases it', () => {
    setUp({
      loading: false,
      playlist: {
        id: 1,
        name: 'All',
        imageRef: '',
        musics: [track(1)],
      },
      email: 'eduardo@example.com',
    });
    const user = fixture.debugElement.query(By.css('.harbour__user'));
    expect(user.nativeElement.textContent.trim()).toBe('EDUARDO.');
  });

  it('falls back to FRIEND when no email is available', () => {
    setUp({
      loading: false,
      playlist: {
        id: 1,
        name: 'All',
        imageRef: '',
        musics: [track(1)],
      },
      email: null,
    });
    const user = fixture.debugElement.query(By.css('.harbour__user'));
    expect(user.nativeElement.textContent.trim()).toBe('FRIEND.');
  });
});
