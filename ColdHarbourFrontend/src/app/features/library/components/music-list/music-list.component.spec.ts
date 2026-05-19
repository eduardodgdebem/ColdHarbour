import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MusicListComponent } from './music-list.component';
import { MusicService } from '../../../player/services/music.service';
import { LibraryService } from '../../library.service';
import type { Music, Playlist } from '../../../../core/api/api.service';
import { By } from '@angular/platform-browser';
import { signal } from '@angular/core';

describe('MusicListComponent', () => {
  let component: MusicListComponent;
  let fixture: ComponentFixture<MusicListComponent>;
  let musicService: jasmine.SpyObj<MusicService>;
  let libraryService: jasmine.SpyObj<LibraryService>;

  const mockMusic: Music = {
    id: 1,
    trackId: '33333333-0000-0000-0000-000000000001',
    albumId: '22222222-0000-0000-0000-000000000001',
    name: 'Test Song',
    author: 'Test Artist',
    audioRef: '/api/stream/33333333-0000-0000-0000-000000000001',
    imageRef: '/api/artwork/22222222-0000-0000-0000-000000000001',
    durationSeconds: 180,
  };

  const mockPlaylist: Playlist = {
    id: 1,
    name: 'Test Playlist',
    imageRef: '/api/artwork/22222222-0000-0000-0000-000000000001',
    musics: [mockMusic]
  };

  beforeEach(async () => {
    const musicSpy = jasmine.createSpyObj('MusicService',
      ['selectMusic', 'isCurrentMusic'],
      {
        currentMusic: signal<Music | null>(null),
        currentPlayList: signal<Playlist | null>(mockPlaylist),
        isLoading: signal(false),
        error: signal<string | null>(null)
      }
    );

    const librarySpy = jasmine.createSpyObj('LibraryService',
      ['uploadFile', 'deleteTrack', 'previewSync', 'applySync'],
      {
        isUploading: signal(false),
        uploadProgress: signal(0),
        syncDiff: signal(null),
      }
    );

    await TestBed.configureTestingModule({
      imports: [MusicListComponent],
      providers: [
        { provide: MusicService, useValue: musicSpy },
        { provide: LibraryService, useValue: librarySpy },
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(MusicListComponent);
    component = fixture.componentInstance;
    musicService = TestBed.inject(MusicService) as jasmine.SpyObj<MusicService>;
    libraryService = TestBed.inject(LibraryService) as jasmine.SpyObj<LibraryService>;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should call selectMusic when selecting a music item', () => {
    component.selectMusic(mockMusic);
    expect(musicService.selectMusic).toHaveBeenCalledWith(mockMusic);
  });

  it('should check if music is current music', () => {
    musicService.isCurrentMusic.and.returnValue(true);
    expect(component.isCurrentMusic(mockMusic)).toBeTrue();
    expect(musicService.isCurrentMusic).toHaveBeenCalledWith(mockMusic);
  });

  it('should return false when music is not current', () => {
    musicService.isCurrentMusic.and.returnValue(false);
    expect(component.isCurrentMusic(mockMusic)).toBeFalse();
  });

  it('should display music list from service', () => {
    const musicList = [
      { ...mockMusic, name: 'Song 1' },
      { ...mockMusic, name: 'Song 2' },
    ];
    musicService.currentPlayList.set({ ...mockPlaylist, musics: musicList });
    fixture.detectChanges();

    const musicElements = fixture.debugElement.queryAll(By.css('.list-item'));
    expect(musicElements.length).toBe(2);
  });
});
