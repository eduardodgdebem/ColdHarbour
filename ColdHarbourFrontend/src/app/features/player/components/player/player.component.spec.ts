import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PlayerComponent } from './player.component';
import { AudioService } from '../../services/audio.service';
import { MusicService } from '../../services/music.service';
import { PlaybackSessionService } from '../../services/playback-session.service';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import type { Music } from '../../../../core/api/api.service';

describe('PlayerComponent', () => {
  let component: PlayerComponent;
  let fixture: ComponentFixture<PlayerComponent>;
  let audioService: jasmine.SpyObj<AudioService>;
  let musicService: jasmine.SpyObj<MusicService>;
  let playbackSession: jasmine.SpyObj<PlaybackSessionService>;

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
      ['selectMusic', 'isCurrentMusic'],
      { currentMusic: signal(null) },
    );

    const playbackSpy = jasmine.createSpyObj('PlaybackSessionService', [
      'next',
      'previous',
      'seek',
      'pause',
      'resume',
    ]);

    await TestBed.configureTestingModule({
      imports: [PlayerComponent],
      providers: [
        provideRouter([]),
        { provide: AudioService, useValue: audioSpy },
        { provide: MusicService, useValue: musicSpy },
        { provide: PlaybackSessionService, useValue: playbackSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PlayerComponent);
    component = fixture.componentInstance;
    audioService = TestBed.inject(AudioService) as jasmine.SpyObj<AudioService>;
    musicService = TestBed.inject(MusicService) as jasmine.SpyObj<MusicService>;
    playbackSession = TestBed.inject(
      PlaybackSessionService,
    ) as jasmine.SpyObj<PlaybackSessionService>;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should update volume input style when volume changes', () => {
    const volumeInput = document.createElement('input');
    component.volumeInput = { nativeElement: volumeInput } as any;

    audioService.volume.set(0.75);
    TestBed.flushEffects();

    expect(volumeInput.style.getPropertyValue('--volume')).toBe('75%');
  });

  it('sends pause to the hub when audio is playing and main button is clicked', () => {
    musicService.currentMusic.set(mockMusic);
    audioService.isPlaying.set(true);
    TestBed.flushEffects();

    const button = document.createElement('button');
    const event = new Event('click');
    Object.defineProperty(event, 'target', { value: button });
    component.mainButtonClick(event);

    expect(playbackSession.pause).toHaveBeenCalled();
    expect(playbackSession.resume).not.toHaveBeenCalled();
    expect(audioService.playToggle).not.toHaveBeenCalled();
  });

  it('sends resume to the hub when audio is paused and main button is clicked', () => {
    musicService.currentMusic.set(mockMusic);
    audioService.isPlaying.set(false);
    TestBed.flushEffects();

    const button = document.createElement('button');
    const event = new Event('click');
    Object.defineProperty(event, 'target', { value: button });
    component.mainButtonClick(event);

    expect(playbackSession.resume).toHaveBeenCalled();
  });

  it('does not touch the hub when no music is selected', () => {
    const button = document.createElement('button');
    const event = new Event('click');
    Object.defineProperty(event, 'target', { value: button });
    component.mainButtonClick(event);

    expect(playbackSession.pause).not.toHaveBeenCalled();
    expect(playbackSession.resume).not.toHaveBeenCalled();
  });

  it('routes progress slider changes through playbackSession.seek (ms)', () => {
    const mockEvent = new Event('change');
    const mockInput = document.createElement('input');
    mockInput.value = '60';
    Object.defineProperty(mockEvent, 'target', { value: mockInput });

    component.onInputChange(mockEvent);

    expect(playbackSession.seek).toHaveBeenCalledWith(60_000);
    expect(audioService.seekTo).not.toHaveBeenCalled();
  });

  it('routes next/previous buttons through the hub', () => {
    component.nextClick();
    component.previousClick();
    expect(playbackSession.next).toHaveBeenCalled();
    expect(playbackSession.previous).toHaveBeenCalled();
  });

  it('keeps local volume control unchanged (not server-managed in Phase 2)', () => {
    const mockEvent = new Event('change');
    const mockInput = document.createElement('input');
    mockInput.value = '0.5';
    Object.defineProperty(mockEvent, 'target', { value: mockInput });

    component.onVolumeChange(mockEvent);

    expect(audioService.setVolume).toHaveBeenCalledWith(0.5);
  });
});
