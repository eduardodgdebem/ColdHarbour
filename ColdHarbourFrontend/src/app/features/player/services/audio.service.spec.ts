import { TestBed, fakeAsync } from '@angular/core/testing';
import { AudioService } from './audio.service';

describe('AudioService', () => {
  let service: AudioService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(AudioService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should initialize with default values', () => {
    expect(service.isPlaying()).toBeFalse();
    expect(service.currentTime()).toBe(0);
    expect(service.duration()).toBe(0);
    expect(service.volume()).toBe(1);
  });

  it('should load music and set duration', (done) => {
    const mockAudio = new Audio();
    Object.defineProperty(mockAudio, 'duration', {
      value: 180,
      writable: false,
    });
    spyOn(window, 'Audio').and.returnValue(mockAudio);
    spyOn(mockAudio, 'play');

    service.loadMusic('test.mp3');
    mockAudio.dispatchEvent(new Event('loadeddata'));

    setTimeout(() => {
      expect(service.duration()).toBe(180);
      done();
    });
  });

  it('loadMusic does NOT auto-play (caller controls when to start)', () => {
    const mockAudio = new Audio();
    spyOn(window, 'Audio').and.returnValue(mockAudio);
    spyOn(mockAudio, 'play');

    service.loadMusic('test.mp3');

    expect(service.isPlaying()).toBeFalse();
    expect(mockAudio.play).not.toHaveBeenCalled();
  });

  it('play() starts the audio element', () => {
    const mockAudio = new Audio();
    spyOn(window, 'Audio').and.returnValue(mockAudio);
    spyOn(mockAudio, 'play');

    service.loadMusic('test.mp3');
    service.play();

    expect(service.isPlaying()).toBeTrue();
    expect(mockAudio.play).toHaveBeenCalled();
  });

  it('play() is idempotent when already playing', () => {
    const mockAudio = new Audio();
    spyOn(window, 'Audio').and.returnValue(mockAudio);
    spyOn(mockAudio, 'play');

    service.loadMusic('test.mp3');
    service.play();
    service.play();

    expect(mockAudio.play).toHaveBeenCalledTimes(1);
  });

  it('pause() stops the audio element', () => {
    const mockAudio = new Audio();
    spyOn(window, 'Audio').and.returnValue(mockAudio);
    spyOn(mockAudio, 'play');
    spyOn(mockAudio, 'pause');

    service.loadMusic('test.mp3');
    service.play();
    service.pause();

    expect(service.isPlaying()).toBeFalse();
    expect(mockAudio.pause).toHaveBeenCalled();
  });

  it('pause() is idempotent when already paused', () => {
    const mockAudio = new Audio();
    spyOn(window, 'Audio').and.returnValue(mockAudio);
    spyOn(mockAudio, 'pause');

    service.loadMusic('test.mp3');
    service.pause();

    expect(mockAudio.pause).not.toHaveBeenCalled();
  });

  it('playToggle alternates between play and pause', () => {
    const mockAudio = new Audio();
    spyOn(window, 'Audio').and.returnValue(mockAudio);
    spyOn(mockAudio, 'play');
    spyOn(mockAudio, 'pause');

    service.loadMusic('test.mp3');
    service.playToggle(); // play
    expect(service.isPlaying()).toBeTrue();
    service.playToggle(); // pause
    expect(service.isPlaying()).toBeFalse();
    expect(mockAudio.play).toHaveBeenCalledTimes(1);
    expect(mockAudio.pause).toHaveBeenCalledTimes(1);
  });

  it('should seek to specified time', () => {
    const mockAudio = new Audio();
    spyOn(window, 'Audio').and.returnValue(mockAudio);
    spyOn(mockAudio, 'play');

    service.loadMusic('test.mp3');
    service.seekTo(60);

    expect(mockAudio.currentTime).toBe(60);
    expect(service.currentTime()).toBe(60);
  });

  it('should set volume within valid range', () => {
    const mockAudio = new Audio();
    spyOn(window, 'Audio').and.returnValue(mockAudio);
    spyOn(mockAudio, 'play');

    service.loadMusic('test.mp3');

    service.setVolume(0.5);
    expect(service.volume()).toBe(0.5);

    service.setVolume(1.5);
    expect(service.volume()).toBe(1);

    service.setVolume(-0.5);
    expect(service.volume()).toBe(0);
  });

  it('should pause audio on cleanup()', fakeAsync(() => {
    const mockAudio = new Audio();
    spyOn(window, 'Audio').and.returnValue(mockAudio);
    spyOn(mockAudio, 'play');
    spyOn(mockAudio, 'pause');

    service.loadMusic('test.mp3');
    service.play();
    service.cleanup();

    expect(mockAudio.pause).toHaveBeenCalled();
    expect(service.isPlaying()).toBeFalse();
  }));
});
