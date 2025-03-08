import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PlayerComponent } from './player.component';
import { AudioService } from '../../services/audio.service';
import { MusicService } from '../../services/music.service';
import { signal } from '@angular/core';
import type { Music } from '../../services/music.service';

describe('PlayerComponent', () => {
  let component: PlayerComponent;
  let fixture: ComponentFixture<PlayerComponent>;
  let audioService: jasmine.SpyObj<AudioService>;
  let musicService: jasmine.SpyObj<MusicService>;

  const mockMusic: Music = {
    id: 1,
    name: 'Test Song',
    author: 'Test Artist',
    audioRef: '/test.mp3',
    imageRef: '/test.jpg'
  };

  beforeEach(async () => {
    const audioSpy = jasmine.createSpyObj('AudioService', ['loadMusic', 'playToggle', 'seekTo', 'setVolume', 'duration'], {
      isPlaying: signal(false),
      volume: signal(0.5),
      sliderDuration: signal(100),
      sliderCurrentTime: signal(0)
    });

    const musicSpy = jasmine.createSpyObj('MusicService', ['currentMusic'], {
      currentMusic: signal(mockMusic)
    });

    await TestBed.configureTestingModule({
      imports: [PlayerComponent],
      providers: [
        { provide: AudioService, useValue: audioSpy },
        { provide: MusicService, useValue: musicSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(PlayerComponent);
    component = fixture.componentInstance;
    audioService = TestBed.inject(AudioService) as jasmine.SpyObj<AudioService>;
    musicService = TestBed.inject(MusicService) as jasmine.SpyObj<MusicService>;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should load music when current music changes', () => {
    expect(audioService.loadMusic).toHaveBeenCalledWith(mockMusic.audioRef);
    expect(audioService.isPlaying.set).toHaveBeenCalledWith(false);
  });

  it('should update volume input style when volume changes', () => {
    const volumeInput = document.createElement('input');
    component.volumeInput = { nativeElement: volumeInput } as any;
    
    audioService.volume.set(0.75);
    fixture.detectChanges();
    
    expect(volumeInput.style.getPropertyValue('--volume')).toBe('75%');
  });

  it('should toggle play/pause on main button click when music is available', () => {
    component.mainButtonClick({} as Event);
    expect(audioService.playToggle).toHaveBeenCalled();
  });

  it('should not toggle play/pause on main button click when no music is available', () => {
    const noMusicSpy = jasmine.createSpyObj('MusicService', ['currentMusic'], {
      currentMusic: signal(null)
    });
    TestBed.overrideProvider(MusicService, { useValue: noMusicSpy });
    fixture = TestBed.createComponent(PlayerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    
    component.mainButtonClick({} as Event);
    expect(audioService.playToggle).not.toHaveBeenCalled();
  });

  it('should update current time on input change', () => {
    const mockEvent = new Event('change');
    const mockInput = document.createElement('input');
    mockInput.value = '60';
    Object.defineProperty(mockEvent, 'target', { value: mockInput });
    
    component.onInputChange(mockEvent);
    
    expect(audioService.seekTo).toHaveBeenCalledWith(60);
  });

  it('should update current time on slider click', () => {
    audioService.duration.and.returnValue(100);
    
    const mockEvent = new MouseEvent('click', {
      clientX: 50
    });
    const mockWrapper = document.createElement('div');
    spyOn(mockWrapper, 'getBoundingClientRect').and.returnValue({
      left: 0,
      width: 100
    } as DOMRect);
    
    Object.defineProperty(mockEvent, 'currentTarget', { value: mockWrapper });
    component.onSliderClick(mockEvent);
    
    expect(audioService.seekTo).toHaveBeenCalledWith(50);
  });

  it('should update volume on volume change', () => {
    const mockEvent = new Event('change');
    const mockInput = document.createElement('input');
    mockInput.value = '0.5';
    Object.defineProperty(mockEvent, 'target', { value: mockInput });
    
    component.onVolumeChange(mockEvent);
    
    expect(audioService.setVolume).toHaveBeenCalledWith(0.5);
  });

  it('should handle keyboard space bar to toggle play/pause', () => {
    const spaceEvent = new KeyboardEvent('keydown', { code: 'Space' });
    document.dispatchEvent(spaceEvent);
    
    expect(audioService.playToggle).toHaveBeenCalled();
  });

  it('should handle keyboard controls', () => {
    const mockEvent = new Event('keydown');
    const volumeInput = document.createElement('input');
    component.volumeInput = { nativeElement: volumeInput } as any;
    
    // Test volume up
    Object.defineProperty(mockEvent, 'key', { value: 'ArrowUp' });
    document.dispatchEvent(mockEvent);
    expect(audioService.volume()).toBe(0.6);
    
    // Test volume down
    Object.defineProperty(mockEvent, 'key', { value: 'ArrowDown' });
    document.dispatchEvent(mockEvent);
    expect(audioService.volume()).toBe(0.4);
  });

  it('should clamp volume between 0 and 1', () => {
    const mockEvent = new Event('change');
    const mockInput = document.createElement('input');
    component.volumeInput = { nativeElement: mockInput } as any;
    
    // Test upper bound
    mockInput.value = '1.5';
    Object.defineProperty(mockEvent, 'target', { value: mockInput });
    component.onVolumeChange(mockEvent);
    expect(audioService.setVolume).toHaveBeenCalledWith(1);
    
    // Test lower bound
    mockInput.value = '-0.5';
    Object.defineProperty(mockEvent, 'target', { value: mockInput });
    component.onVolumeChange(mockEvent);
    expect(audioService.setVolume).toHaveBeenCalledWith(0);
  });

  it('should handle audio loading errors', () => {
    const errorSpy = spyOn(console, 'error');
    audioService.loadMusic.and.throwError('Failed to load audio');
    
    musicService.currentMusic.set({...mockMusic});
    
    expect(errorSpy).toHaveBeenCalled();
    expect(audioService.isPlaying.set).toHaveBeenCalledWith(false);
  });

  it('should update progress during playback', () => {
    const duration = 100;
    const currentTime = 50;
    audioService.duration.and.returnValue(duration);
    
    const mockEvent = new MouseEvent('click', {
      clientX: currentTime
    });
    const mockWrapper = document.createElement('div');
    spyOn(mockWrapper, 'getBoundingClientRect').and.returnValue({
      left: 0,
      width: duration
    } as DOMRect);
    
    Object.defineProperty(mockEvent, 'currentTarget', { value: mockWrapper });
    component.onSliderClick(mockEvent);
    
    expect(audioService.seekTo).toHaveBeenCalledWith(50);
  });
});
