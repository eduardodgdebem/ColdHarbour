import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ApiService } from './api.service';
import type { Music, Playlist } from './music.service';

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
      providers: [ApiService]
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
    const serverMusic = makeMusic({ audioRef: '/api/stream/track-1', imageRef: '/api/artwork/album-1' });
    const serverResponse: Playlist = { id: 1, name: 'Test Playlist', imageRef: '/api/artwork/album-1', musics: [serverMusic] };

    service.getPlaylist(1).subscribe(playlist => {
      expect(playlist.musics[0].audioRef).toContain('/api/stream/track-1');
      expect(playlist.musics[0].imageRef).toContain('/api/artwork/album-1');
    });

    const req = httpMock.expectOne('http://localhost:8080/api/music/playlist/1');
    expect(req.request.method).toBe('GET');
    req.flush(serverResponse);
  });

  it('should handle empty playlist', () => {
    const serverResponse: Playlist = { id: 1, name: 'Test Playlist', imageRef: '', musics: [] };

    service.getPlaylist(1).subscribe(playlist => {
      expect(playlist.musics.length).toBe(0);
    });

    const req = httpMock.expectOne('http://localhost:8080/api/music/playlist/1');
    expect(req.request.method).toBe('GET');
    req.flush(serverResponse);
  });

  it('should handle multiple items in playlist', () => {
    const musics: Music[] = [
      makeMusic({ id: 1, trackId: 'track-1', name: 'Song 1', author: 'Artist 1', audioRef: '/api/stream/track-1', imageRef: '/api/artwork/album-1' }),
      makeMusic({ id: 2, trackId: 'track-2', name: 'Song 2', author: 'Artist 2', audioRef: '/api/stream/track-2', imageRef: '/api/artwork/album-2' }),
    ];
    const serverResponse: Playlist = { id: 1, name: 'Test Playlist', imageRef: '', musics };

    service.getPlaylist(1).subscribe(playlist => {
      expect(playlist.musics.length).toBe(2);
      expect(playlist.musics[0].audioRef).toContain('/api/stream/track-1');
      expect(playlist.musics[1].imageRef).toContain('/api/artwork/album-2');
    });

    const req = httpMock.expectOne('http://localhost:8080/api/music/playlist/1');
    expect(req.request.method).toBe('GET');
    req.flush(serverResponse);
  });
});
