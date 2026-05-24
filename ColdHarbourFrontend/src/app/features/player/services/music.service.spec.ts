import { TestBed } from '@angular/core/testing';
import { MusicService } from './music.service';
import { ApiService } from '../../../core/api/api.service';
import { ColorService } from './color.service';
import { of, throwError } from 'rxjs';
import type { Music, Playlist } from '../../../core/api/api.service';

describe('MusicService', () => {
  let service: MusicService;
  let apiService: jasmine.SpyObj<ApiService>;
  let colorService: jasmine.SpyObj<ColorService>;

  const mockMusic: Music = {
    id: 1,
    trackId: '33333333-0000-0000-0000-000000000001',
    albumId: '22222222-0000-0000-0000-000000000001',
    name: "Baby You're Bad",
    author: 'HONNE',
    audioRef: '/api/stream/33333333-0000-0000-0000-000000000001',
    imageRef: '/api/artwork/22222222-0000-0000-0000-000000000001',
    durationSeconds: 210,
  };

  const mockPlaylist: Playlist = {
    id: 1,
    name: 'Test Playlist',
    imageRef: '/api/artwork/22222222-0000-0000-0000-000000000001',
    musics: [mockMusic],
  };

  beforeEach(() => {
    const apiSpy = jasmine.createSpyObj('ApiService', ['getPlaylist']);
    apiSpy.getPlaylist.and.returnValue(of(mockPlaylist));
    const colorSpy = jasmine.createSpyObj('ColorService', ['extractColor']);

    TestBed.configureTestingModule({
      providers: [
        MusicService,
        { provide: ApiService, useValue: apiSpy },
        { provide: ColorService, useValue: colorSpy },
      ],
    });

    service = TestBed.inject(MusicService);
    apiService = TestBed.inject(ApiService) as jasmine.SpyObj<ApiService>;
    colorService = TestBed.inject(ColorService) as jasmine.SpyObj<ColorService>;
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should initialize with loading state', () => {
    expect(service.isLoading()).toBeTrue();
    expect(service.error()).toBeNull();
    expect(service.currentMusic()).toBeNull();
    expect(service.currentPlayList()).toBeNull();
  });

  it('should load playlist successfully after setCurrentPlaylist', () => {
    service.setCurrentPlaylist(1);

    expect(service.currentPlayList()).toEqual(mockPlaylist);
    expect(service.isLoading()).toBeFalse();
    expect(service.error()).toBeNull();
  });

  it('should handle playlist loading error', () => {
    apiService.getPlaylist.and.returnValue(
      throwError(() => new Error('Network error')),
    );

    service.setCurrentPlaylist(1);

    expect(service.currentPlayList()).toBeNull();
    expect(service.isLoading()).toBeFalse();
    expect(service.error()).toBe('Failed to load playlist');
  });

  it('should select music', () => {
    service.selectMusic(mockMusic);
    expect(service.currentMusic()).toEqual(mockMusic);
  });

  it('should correctly identify current music', () => {
    service.selectMusic(mockMusic);

    expect(service.isCurrentMusic(mockMusic)).toBeTrue();
    expect(service.isCurrentMusic({ ...mockMusic, id: 2 })).toBeFalse();
  });

  it('should handle isCurrentMusic check when no music is selected', () => {
    expect(service.isCurrentMusic(mockMusic)).toBeFalse();
  });

  it('does NOT auto-load audio on currentMusic change (PlaybackSessionService owns that now)', () => {
    // Audio loading was moved into PlaybackSessionService's active-device
    // effect so non-active devices stop playing in parallel when their user
    // picks a track. MusicService is purely UI/data now.
    service.selectMusic(mockMusic);
    TestBed.flushEffects();
    // No AudioService dependency anymore — nothing to assert here besides
    // the fact that this spec configures the TestBed without AudioService
    // and the service still constructs.
    expect(service.currentMusic()).toEqual(mockMusic);
  });

  it('asks ColorService to extract the album art color when currentMusic changes', () => {
    service.selectMusic(mockMusic);
    TestBed.flushEffects();
    expect(colorService.extractColor).toHaveBeenCalledWith(mockMusic.imageRef);
  });
});
