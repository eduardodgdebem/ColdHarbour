import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MusicListComponent } from './music-list.component';
import { MusicService } from '../../services/music.service';
import type { Music, Playlist } from '../../services/music.service';
import { By } from '@angular/platform-browser';
import { signal } from '@angular/core';

describe('MusicListComponent', () => {
  let component: MusicListComponent;
  let fixture: ComponentFixture<MusicListComponent>;
  let musicService: jasmine.SpyObj<MusicService>;

  const mockMusic: Music = {
    id: 1,
    name: 'Test Song',
    author: 'Test Artist',
    audioRef: '/test.mp3',
    imageRef: '/test.jpg'
  };

  const mockPlaylist: Playlist = {
    id: 1,
    name: 'Test Playlist',
    imageRef: '/test.jpg',
    musics: [mockMusic]
  };

  beforeEach(async () => {
    const spy = jasmine.createSpyObj('MusicService', 
      ['selectMusic', 'isCurrentMusic', 'isLoading', 'error'],
      {
        currentMusic: signal(null),
        currentPlayList: signal(mockPlaylist),
        isLoading: signal(false),
        error: signal(null)
      }
    );
    
    await TestBed.configureTestingModule({
      imports: [MusicListComponent],
      providers: [
        { provide: MusicService, useValue: spy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(MusicListComponent);
    component = fixture.componentInstance;
    musicService = TestBed.inject(MusicService) as jasmine.SpyObj<MusicService>;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should call selectMusic when selecting a music item', () => {
    musicService.selectMusic.and.returnValue(undefined);
    
    component.selectMusic(mockMusic);
    
    expect(musicService.selectMusic).toHaveBeenCalledWith(mockMusic);
  });

  it('should check if music is current music', () => {
    musicService.isCurrentMusic.and.returnValue(true);
    
    const result = component.isCurrentMusic(mockMusic);
    
    expect(result).toBe(true);
    expect(musicService.isCurrentMusic).toHaveBeenCalledWith(mockMusic);
  });

  it('should return false when music is not current', () => {
    musicService.isCurrentMusic.and.returnValue(false);
    
    const result = component.isCurrentMusic(mockMusic);
    
    expect(result).toBe(false);
    expect(musicService.isCurrentMusic).toHaveBeenCalledWith(mockMusic);
  });

  it('should display music list from service', () => {
    const mockMusicList = [
      { ...mockMusic, name: 'Song 1' },
      { ...mockMusic, name: 'Song 2' },
      { ...mockMusic, name: 'Song 3' }
    ];
    
    musicService.currentPlayList.set({ ...mockPlaylist, musics: mockMusicList });
    fixture.detectChanges();
    
    const musicElements = fixture.debugElement.queryAll(By.css('.list-item'));
    expect(musicElements.length).toBe(3);
    expect(musicElements[0].nativeElement.textContent).toContain('Song 1');
  });

  it('should highlight current playing music', () => {
    musicService.currentMusic.set(mockMusic);
    musicService.isCurrentMusic.and.returnValue(true);
    fixture.detectChanges();
    
    const musicElement = fixture.debugElement.query(By.css('.list-item.active'));
    expect(musicElement).toBeTruthy();
  });

  it('should show loading state while loading playlist', () => {
    musicService.isLoading.and.returnValue(true);
    fixture.detectChanges();
    
    const loadingElement = fixture.debugElement.query(By.css('.loading'));
    expect(loadingElement).toBeTruthy();
    expect(loadingElement.nativeElement.textContent).toContain('Loading playlist...');
  });

  it('should show error state when loading fails', () => {
    const errorMessage = 'Failed to load music list';
    musicService.error.and.returnValue(errorMessage);
    fixture.detectChanges();
    
    const errorElement = fixture.debugElement.query(By.css('.error'));
    expect(errorElement).toBeTruthy();
    expect(errorElement.nativeElement.textContent).toContain(errorMessage);
  });
});
