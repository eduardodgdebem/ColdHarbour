import { TestBed } from '@angular/core/testing';
import { MusicService } from './music.service';
import { ApiService } from './api.service';
import { ColorService } from './color.service';
import { of, throwError } from 'rxjs';

describe('MusicService', () => {
  let service: MusicService;
  let apiService: jasmine.SpyObj<ApiService>;
  let colorService: jasmine.SpyObj<ColorService>;

  const mockMusic = {
    id: 1,
    name: "Baby You're Bad",
    author: "HONNE",
    audioRef: "/Baby You're Bad - HONNE.mp3",
    imageRef: "/babyyourebad.jpg"
  };

  beforeEach(() => {
    const apiSpy = jasmine.createSpyObj('ApiService', ['getPlaylist']);
    const colorSpy = jasmine.createSpyObj('ColorService', ['extractColor']);

    TestBed.configureTestingModule({
      providers: [
        MusicService,
        { provide: ApiService, useValue: apiSpy },
        { provide: ColorService, useValue: colorSpy }
      ]
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
    expect(service.currentPlayList()).toEqual([]);
  });

  it('should load playlist successfully', () => {
    const mockPlaylist = [mockMusic];
    apiService.getPlaylist.and.returnValue(of(mockPlaylist));

    // Trigger loadPlaylist by creating a new instance
    service = new MusicService(apiService, colorService);

    expect(service.currentPlayList()).toEqual(mockPlaylist);
    expect(service.isLoading()).toBeFalse();
    expect(service.error()).toBeNull();
  });

  it('should handle playlist loading error', () => {
    const error = new Error('Network error');
    apiService.getPlaylist.and.returnValue(throwError(() => error));

    // Trigger loadPlaylist by creating a new instance
    service = new MusicService(apiService, colorService);

    expect(service.currentPlayList()).toEqual([]);
    expect(service.isLoading()).toBeFalse();
    expect(service.error()).toBe('Failed to load playlist');
  });

  it('should select music and extract color', () => {
    service.selectMusic(mockMusic);

    expect(service.currentMusic()).toEqual(mockMusic);
    expect(colorService.extractColor).toHaveBeenCalledWith(mockMusic.imageRef);
  });

  it('should handle spaces in image URL when extracting color', () => {
    const musicWithSpaces = {
      ...mockMusic,
      imageRef: '/image with spaces.jpg'
    };

    service.selectMusic(musicWithSpaces);

    expect(colorService.extractColor).toHaveBeenCalledWith('/image%20with%20spaces.jpg');
  });

  it('should correctly identify current music', () => {
    service.selectMusic(mockMusic);

    const result = service.isCurrentMusic(mockMusic);
    expect(result).toBeTrue();

    const differentMusic = { ...mockMusic, id: 2 };
    const resultDifferent = service.isCurrentMusic(differentMusic);
    expect(resultDifferent).toBeFalse();
  });

  it('should handle isCurrentMusic check when no music is selected', () => {
    const result = service.isCurrentMusic(mockMusic);
    expect(result).toBeFalse();
  });
});
