import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { AudioService } from './audio.service';
import { DestroyRef } from '@angular/core';

describe('AudioService', () => {
  let service: AudioService;
  let destroyRef: jasmine.SpyObj<DestroyRef>;

  beforeEach(() => {
    const destroySpy = jasmine.createSpyObj('DestroyRef', ['onDestroy']);
    
    TestBed.configureTestingModule({
      providers: [
        { provide: DestroyRef, useValue: destroySpy }
      ]
    });
    service = TestBed.inject(AudioService);
    destroyRef = TestBed.inject(DestroyRef) as jasmine.SpyObj<DestroyRef>;
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
      writable: false
    });
    spyOn(window, 'Audio').and.returnValue(mockAudio);
    
    service.loadMusic('test.mp3');
    mockAudio.dispatchEvent(new Event('loadeddata'));
    
    setTimeout(() => {
      expect(service.duration()).toBe(180);
      done();
    });
  });

  it('should toggle play/pause state', fakeAsync(() => {
    const mockAudio = new Audio();
    spyOn(window, 'Audio').and.returnValue(mockAudio);
    spyOn(mockAudio, 'play');
    spyOn(mockAudio, 'pause');
    
    service.loadMusic('test.mp3');
    
    service.playToggle();
    expect(service.isPlaying()).toBeTrue();
    expect(mockAudio.play).toHaveBeenCalled();
    
    service.playToggle();
    expect(service.isPlaying()).toBeFalse();
    expect(mockAudio.pause).toHaveBeenCalled();
  }));

  it('should seek to specified time', () => {
    const mockAudio = new Audio();
    spyOn(window, 'Audio').and.returnValue(mockAudio);
    
    service.loadMusic('test.mp3');
    service.seekTo(60);
    
    expect(mockAudio.currentTime).toBe(60);
    expect(service.currentTime()).toBe(60);
  });

  it('should set volume within valid range', () => {
    const mockAudio = new Audio();
    spyOn(window, 'Audio').and.returnValue(mockAudio);
    
    service.loadMusic('test.mp3');
    
    service.setVolume(0.5);
    expect(service.volume()).toBe(0.5);
    
    service.setVolume(1.5);
    expect(service.volume()).toBe(1);
    
    service.setVolume(-0.5);
    expect(service.volume()).toBe(0);
  });

  it('should clean up on destroy', () => {
    const mockAudio = new Audio();
    spyOn(window, 'Audio').and.returnValue(mockAudio);
    spyOn(mockAudio, 'pause');
    
    service.loadMusic('test.mp3');
    
    // Get the destroy callback
    const [destroyCallback] = destroyRef.onDestroy.calls.mostRecent().args;
    
    // Call the destroy callback
    destroyCallback();
    
    expect(mockAudio.pause).toHaveBeenCalled();
    expect(mockAudio.src).toBe('');
  });

  it('should update time periodically when playing', fakeAsync(() => {
    const mockAudio = new Audio();
    spyOn(window, 'Audio').and.returnValue(mockAudio);
    
    service.loadMusic('test.mp3');
    service.playToggle();
    
    Object.defineProperty(mockAudio, 'currentTime', {
      value: 10,
      writable: false
    });
    
    tick(100);
    expect(service.currentTime()).toBe(10);
    
    Object.defineProperty(mockAudio, 'currentTime', {
      value: 11,
      writable: false
    });
    
    tick(100);
    expect(service.currentTime()).toBe(11);
  }));
});
