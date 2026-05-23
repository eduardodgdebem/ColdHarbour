import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PlayerComponent } from './player.component';
import { AudioService } from '../../services/audio.service';
import { MusicService } from '../../services/music.service';
import { signal } from '@angular/core';
import type { Music } from '../../../../core/api/api.service';

describe('PlayerComponent', () => {
  let component: PlayerComponent;
  let fixture: ComponentFixture<PlayerComponent>;
  let audioService: jasmine.SpyObj<AudioService>;
  let musicService: jasmine.SpyObj<MusicService>;

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

  beforeEach(async () => {
    const audioSpy = jasmine.createSpyObj(
      'AudioService',
      ['loadMusic', 'playToggle', 'seekTo', 'setVolume', 'cleanup'],
      {
        isPlaying: signal(false),
        volume: signal(0.5),
        currentTime: signal(0),
        duration: signal(0),
        ended: signal(false),
      },
    );

    const musicSpy = jasmine.createSpyObj(
      'MusicService',
      ['selectMusic', 'nextMusic', 'previousMusic', 'isCurrentMusic'],
      { currentMusic: signal(null) },
    );

    await TestBed.configureTestingModule({
      imports: [PlayerComponent],
      providers: [
        { provide: AudioService, useValue: audioSpy },
        { provide: MusicService, useValue: musicSpy },
      ],
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

  // Audio-loading is owned by MusicService now (see music.service.spec.ts);
  // PlayerComponent only mirrors UI state from AudioService.

  it('should update volume input style when volume changes', () => {
    const volumeInput = document.createElement('input');
    component.volumeInput = { nativeElement: volumeInput } as any;

    audioService.volume.set(0.75);
    TestBed.flushEffects();

    expect(volumeInput.style.getPropertyValue('--volume')).toBe('75%');
  });

  it('should call playToggle on main button click when music is available', () => {
    musicService.currentMusic.set(mockMusic);
    TestBed.flushEffects();

    const button = document.createElement('button');
    const event = new Event('click');
    Object.defineProperty(event, 'target', { value: button });
    component.mainButtonClick(event);

    expect(audioService.playToggle).toHaveBeenCalled();
  });

  it('should not call playToggle when no music is selected', () => {
    const button = document.createElement('button');
    const event = new Event('click');
    Object.defineProperty(event, 'target', { value: button });
    component.mainButtonClick(event);

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

  it('should update volume on volume change', () => {
    const mockEvent = new Event('change');
    const mockInput = document.createElement('input');
    mockInput.value = '0.5';
    Object.defineProperty(mockEvent, 'target', { value: mockInput });

    component.onVolumeChange(mockEvent);

    expect(audioService.setVolume).toHaveBeenCalledWith(0.5);
  });
});
