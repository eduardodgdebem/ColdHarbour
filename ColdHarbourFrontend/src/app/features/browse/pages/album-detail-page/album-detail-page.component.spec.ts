import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { AlbumDetailPageComponent } from './album-detail-page.component';
import { BrowseService } from '../../browse.service';
import { MusicService } from '../../../player/services/music.service';
import { LibraryService } from '../../../library/library.service';
import { PlaybackSessionService } from '../../../player/services/playback-session.service';
import type {
  AlbumDetail,
  Music,
  Playlist,
} from '../../../../core/api/api.service';

function track(id: number): Music {
  return {
    id,
    trackId: `track-${id}`,
    albumId: 'album-1',
    name: `Track ${id}`,
    author: 'Pink Floyd',
    audioRef: `/api/stream/track-${id}`,
    imageRef: '/api/artwork/album-1',
    durationSeconds: 200,
  };
}

const detail: AlbumDetail = {
  id: 'album-1',
  title: 'The Wall',
  artist: 'Pink Floyd',
  artistId: 'artist-1',
  year: 1979,
  imageRef: '/api/artwork/album-1?size=1024&v=abc',
  tracks: [track(1), track(2)],
};

describe('AlbumDetailPageComponent', () => {
  let fixture: ComponentFixture<AlbumDetailPageComponent>;
  let component: AlbumDetailPageComponent;
  let browse: jasmine.SpyObj<BrowseService>;
  let album: ReturnType<typeof signal<AlbumDetail | null>>;
  let loading: ReturnType<typeof signal<boolean>>;
  let error: ReturnType<typeof signal<string | null>>;

  function setUp(paramId = 'album-1') {
    album = signal<AlbumDetail | null>(null);
    loading = signal(false);
    error = signal<string | null>(null);
    browse = jasmine.createSpyObj(
      'BrowseService',
      ['loadAlbum', 'saveAlbum', 'saveTrack'],
      {
        album,
        albumLoading: loading,
        albumError: error,
        saving: signal(false),
        saveError: signal<string | null>(null),
      },
    );

    const musicSpy = jasmine.createSpyObj(
      'MusicService',
      ['selectMusic', 'isCurrentMusic'],
      {
        currentMusic: signal<Music | null>(null),
        currentPlayList: signal<Playlist | null>(null),
        isLoading: signal(false),
        error: signal<string | null>(null),
      },
    );
    const librarySpy = jasmine.createSpyObj('LibraryService', ['deleteTrack'], {
      isUploading: signal(false),
      uploadError: signal<string | null>(null),
      isSyncing: signal(false),
      syncDiff: signal(null),
      syncError: signal<string | null>(null),
    });
    const playbackSpy = jasmine.createSpyObj('PlaybackSessionService', [
      'setQueue',
      'addToQueue',
    ]);

    TestBed.configureTestingModule({
      imports: [AlbumDetailPageComponent],
      providers: [
        provideRouter([]),
        { provide: BrowseService, useValue: browse },
        { provide: MusicService, useValue: musicSpy },
        { provide: LibraryService, useValue: librarySpy },
        { provide: PlaybackSessionService, useValue: playbackSpy },
        {
          provide: ActivatedRoute,
          useValue: { paramMap: of(convertToParamMap({ id: paramId })) },
        },
      ],
    });
    fixture = TestBed.createComponent(AlbumDetailPageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('loads the album from the route param on init', () => {
    setUp('album-9');
    expect(browse.loadAlbum).toHaveBeenCalledWith('album-9');
  });

  it('renders the album title, artist and the track list', () => {
    setUp();
    album.set(detail);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('The Wall');
    expect(el.textContent).toContain('Pink Floyd');
    expect(fixture.debugElement.query(By.css('app-music-list'))).toBeTruthy();
  });

  it('links the artist name to the artist detail route', () => {
    setUp();
    album.set(detail);
    fixture.detectChanges();
    const link = fixture.debugElement.query(By.css('a.album-hero__artist'))
      .nativeElement as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('/artists/artist-1');
  });

  it('shows the error state when the album is missing', () => {
    setUp();
    error.set('Album not found.');
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('.browse__error'))).toBeTruthy();
  });

  it('opens the album edit modal and forwards save to the service', () => {
    setUp();
    album.set(detail);
    fixture.detectChanges();

    const editBtn = fixture.debugElement.query(
      By.css('[data-test=edit-album] button'),
    ).nativeElement as HTMLButtonElement;
    editBtn.click();
    fixture.detectChanges();

    component['onAlbumSave']({
      title: 'The Wall',
      year: 1979,
      coverFile: null,
    });

    expect(browse.saveAlbum).toHaveBeenCalled();
    const args = browse.saveAlbum.calls.mostRecent().args;
    expect(args[0]).toBe('album-1');
    expect(args[1]).toEqual({ title: 'The Wall', year: 1979 });
    expect(args[2]).toBeNull();
  });

  it('forwards a song edit to saveTrack with the parent album id', () => {
    setUp();
    album.set(detail);
    fixture.detectChanges();

    component['openSongEdit'](track(1));
    component['onSongSave']({ title: 'Hey You', trackNumber: 2 });

    expect(browse.saveTrack).toHaveBeenCalled();
    const args = browse.saveTrack.calls.mostRecent().args;
    expect(args[0]).toBe('album-1');
    expect(args[1]).toBe('track-1');
    expect(args[2]).toEqual({ title: 'Hey You', trackNumber: 2 });
  });
});
