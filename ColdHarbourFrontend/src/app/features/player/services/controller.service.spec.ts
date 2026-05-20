import { TestBed } from '@angular/core/testing';
import { ControllerService } from './controller.service';
import { AudioService } from './audio.service';
import { MusicService } from './music.service';
import { signal } from '@angular/core';

describe('ControllerService', () => {
  let service: ControllerService;

  beforeEach(() => {
    const audioSpy = jasmine.createSpyObj(
      'AudioService',
      ['playToggle', 'seekTo', 'setVolume'],
      {
        isPlaying: signal(false),
        currentTime: signal(0),
        duration: signal(0),
        volume: signal(1),
        ended: signal(false),
      },
    );

    const musicSpy = jasmine.createSpyObj(
      'MusicService',
      ['nextMusic', 'previousMusic'],
      { currentMusic: signal(null) },
    );

    TestBed.configureTestingModule({
      providers: [
        ControllerService,
        { provide: AudioService, useValue: audioSpy },
        { provide: MusicService, useValue: musicSpy },
      ],
    });
    service = TestBed.inject(ControllerService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
