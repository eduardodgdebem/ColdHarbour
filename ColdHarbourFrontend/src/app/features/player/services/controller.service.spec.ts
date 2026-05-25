import { TestBed } from '@angular/core/testing';
import { ControllerService } from './controller.service';
import { AudioService } from './audio.service';
import { MusicService } from './music.service';
import { PlaybackSessionService } from './playback-session.service';
import { signal } from '@angular/core';

describe('ControllerService', () => {
  let service: ControllerService;
  let playbackSpy: jasmine.SpyObj<PlaybackSessionService>;
  let audioEnded: ReturnType<typeof signal<boolean>>;
  let audioIsPlaying: ReturnType<typeof signal<boolean>>;

  beforeEach(() => {
    audioEnded = signal(false);
    audioIsPlaying = signal(false);
    const audioSpy = jasmine.createSpyObj(
      'AudioService',
      ['playToggle', 'seekTo', 'setVolume'],
      {
        isPlaying: audioIsPlaying,
        currentTime: signal(30),
        duration: signal(180),
        volume: signal(1),
        ended: audioEnded,
      },
    );

    const musicSpy = jasmine.createSpyObj(
      'MusicService',
      ['selectMusic'],
      { currentMusic: signal(null) },
    );

    playbackSpy = jasmine.createSpyObj(
      'PlaybackSessionService',
      ['next', 'previous', 'seek', 'pause', 'resume'],
      { session: signal(null), devices: signal([]) },
    );

    TestBed.configureTestingModule({
      providers: [
        ControllerService,
        { provide: AudioService, useValue: audioSpy },
        { provide: MusicService, useValue: musicSpy },
        { provide: PlaybackSessionService, useValue: playbackSpy },
      ],
    });
    service = TestBed.inject(ControllerService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  // Track-ended handling lives in PlaybackSessionService (phase 3); see
  // playback-session.service.spec.ts. ControllerService no longer listens.
});
