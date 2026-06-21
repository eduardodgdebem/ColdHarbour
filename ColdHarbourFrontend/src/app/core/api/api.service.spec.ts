import { TestBed } from '@angular/core/testing';
import {
  HttpClientTestingModule,
  HttpTestingController,
} from '@angular/common/http/testing';
import { ApiService } from './api.service';
import type { Music, Playlist } from './api.service';

const makeMusic = (overrides: Partial<Music>): Music => ({
  id: 1,
  trackId: 'track-1',
  albumId: 'album-1',
  name: 'Test Song',
  author: 'Test Artist',
  audioRef: '/test.mp3',
  imageRef: '/test.jpg',
  durationSeconds: 180,
  ...overrides,
});

describe('ApiService', () => {
  let service: ApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [ApiService],
    });
    service = TestBed.inject(ApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should get playlist and transform URLs', () => {
    const serverMusic = makeMusic({
      audioRef: '/api/stream/track-1',
      imageRef: '/api/artwork/album-1',
    });
    const serverResponse: Playlist = {
      id: 1,
      name: 'Test Playlist',
      imageRef: '/api/artwork/album-1',
      musics: [serverMusic],
    };

    service.getPlaylist(1).subscribe((playlist) => {
      expect(playlist.musics[0].audioRef).toContain('/api/stream/track-1');
      expect(playlist.musics[0].imageRef).toContain('/api/artwork/album-1');
    });

    const req = httpMock.expectOne('/api/music/playlist/1');
    expect(req.request.method).toBe('GET');
    req.flush(serverResponse);
  });

  it('should handle empty playlist', () => {
    const serverResponse: Playlist = {
      id: 1,
      name: 'Test Playlist',
      imageRef: '',
      musics: [],
    };

    service.getPlaylist(1).subscribe((playlist) => {
      expect(playlist.musics.length).toBe(0);
    });

    const req = httpMock.expectOne('/api/music/playlist/1');
    expect(req.request.method).toBe('GET');
    req.flush(serverResponse);
  });

  it('should handle multiple items in playlist', () => {
    const musics: Music[] = [
      makeMusic({
        id: 1,
        trackId: 'track-1',
        name: 'Song 1',
        author: 'Artist 1',
        audioRef: '/api/stream/track-1',
        imageRef: '/api/artwork/album-1',
      }),
      makeMusic({
        id: 2,
        trackId: 'track-2',
        name: 'Song 2',
        author: 'Artist 2',
        audioRef: '/api/stream/track-2',
        imageRef: '/api/artwork/album-2',
      }),
    ];
    const serverResponse: Playlist = {
      id: 1,
      name: 'Test Playlist',
      imageRef: '',
      musics,
    };

    service.getPlaylist(1).subscribe((playlist) => {
      expect(playlist.musics.length).toBe(2);
      expect(playlist.musics[0].audioRef).toContain('/api/stream/track-1');
      expect(playlist.musics[1].imageRef).toContain('/api/artwork/album-2');
    });

    const req = httpMock.expectOne('/api/music/playlist/1');
    expect(req.request.method).toBe('GET');
    req.flush(serverResponse);
  });

  describe('browse: albums & artists', () => {
    it('getAlbums GETs /api/albums', () => {
      let result: unknown;
      service.getAlbums().subscribe((a) => (result = a));
      const req = httpMock.expectOne('/api/albums');
      expect(req.request.method).toBe('GET');
      req.flush([
        {
          id: 'album-1',
          title: 'The Wall',
          artist: 'Pink Floyd',
          artistId: 'artist-1',
          year: 1979,
          imageRef: '/api/artwork/album-1?size=256&v=abc',
          trackCount: 2,
        },
      ]);
      expect((result as { length: number }).length).toBe(1);
    });

    it('getAlbum GETs /api/albums/{id} and transforms track URLs', () => {
      service.getAlbum('album-1').subscribe((album) => {
        expect(album.title).toBe('The Wall');
        expect(album.tracks[0].audioRef).toContain('/api/stream/track-1');
        expect(album.tracks[0].imageRef).toContain('/api/artwork/album-1');
      });
      const req = httpMock.expectOne('/api/albums/album-1');
      expect(req.request.method).toBe('GET');
      req.flush({
        id: 'album-1',
        title: 'The Wall',
        artist: 'Pink Floyd',
        artistId: 'artist-1',
        year: 1979,
        imageRef: '/api/artwork/album-1?size=256',
        tracks: [
          makeMusic({
            trackId: 'track-1',
            audioRef: '/api/stream/track-1',
            imageRef: '/api/artwork/album-1?size=256',
          }),
        ],
      });
    });

    it('getArtists GETs /api/artists', () => {
      service.getArtists().subscribe();
      const req = httpMock.expectOne('/api/artists');
      expect(req.request.method).toBe('GET');
      req.flush([{ id: 'artist-1', name: 'Pink Floyd', albumCount: 1 }]);
    });

    it('getArtist GETs /api/artists/{id}', () => {
      service.getArtist('artist-1').subscribe((artist) => {
        expect(artist.name).toBe('Pink Floyd');
        expect(artist.albums.length).toBe(1);
      });
      const req = httpMock.expectOne('/api/artists/artist-1');
      expect(req.request.method).toBe('GET');
      req.flush({
        id: 'artist-1',
        name: 'Pink Floyd',
        albums: [
          {
            id: 'album-1',
            title: 'The Wall',
            artist: 'Pink Floyd',
            artistId: 'artist-1',
            year: 1979,
            imageRef: '/api/artwork/album-1?size=256',
            trackCount: 2,
          },
        ],
      });
    });
  });
});
