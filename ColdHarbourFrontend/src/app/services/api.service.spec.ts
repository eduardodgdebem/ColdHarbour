import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ApiService } from './api.service';
import type { Music } from './music.service';

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
    const mockPlaylist: Music[] = [
      {
        id: 1,
        name: 'Test Song',
        author: 'Test Artist',
        audioRef: '/test.mp3',
        imageRef: '/test.jpg'
      }
    ];

    const expectedPlaylist: Music[] = [
      {
        id: 1,
        name: 'Test Song',
        author: 'Test Artist',
        audioRef: 'http://localhost:8080/test.mp3',
        imageRef: 'http://localhost:8080/test.jpg'
      }
    ];

    service.getPlaylist().subscribe(playlist => {
      expect(playlist).toEqual(expectedPlaylist);
    });

    const req = httpMock.expectOne('http://localhost:8080/api/music/playlist');
    expect(req.request.method).toBe('GET');
    req.flush(mockPlaylist);
  });

  it('should handle empty playlist', () => {
    const mockPlaylist: Music[] = [];

    service.getPlaylist().subscribe(playlist => {
      expect(playlist).toEqual([]);
    });

    const req = httpMock.expectOne('http://localhost:8080/api/music/playlist');
    expect(req.request.method).toBe('GET');
    req.flush(mockPlaylist);
  });

  it('should handle multiple items in playlist', () => {
    const mockPlaylist: Music[] = [
      {
        id: 1,
        name: 'Song 1',
        author: 'Artist 1',
        audioRef: '/song1.mp3',
        imageRef: '/image1.jpg'
      },
      {
        id: 2,
        name: 'Song 2',
        author: 'Artist 2',
        audioRef: '/song2.mp3',
        imageRef: '/image2.jpg'
      }
    ];

    service.getPlaylist().subscribe(playlist => {
      expect(playlist.length).toBe(2);
      expect(playlist[0].audioRef).toBe('http://localhost:8080/song1.mp3');
      expect(playlist[1].imageRef).toBe('http://localhost:8080/image2.jpg');
    });

    const req = httpMock.expectOne('http://localhost:8080/api/music/playlist');
    expect(req.request.method).toBe('GET');
    req.flush(mockPlaylist);
  });
}); 