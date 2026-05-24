import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal, WritableSignal } from '@angular/core';
import { Location } from '@angular/common';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { PlayerPageComponent } from './player-page.component';
import { MusicService } from '../../services/music.service';
import { AudioService } from '../../services/audio.service';
import { PlaybackSessionService } from '../../services/playback-session.service';
import type { Music, Playlist } from '../../../../core/api/api.service';

function makeTrack(overrides: Partial<Music> = {}): Music {
  return {
    id: 1,
    trackId: 'track-1',
    albumId: 'album-1',
    name: 'Test Song',
    author: 'Test Artist',
    audioRef: '/api/stream/track-1',
    imageRef: '/api/artwork/album-1',
    durationSeconds: 240,
    ...overrides,
  };
}

describe('PlayerPageComponent', () => {
  let fixture: ComponentFixture<PlayerPageComponent>;
  let component: PlayerPageComponent;
  let musicSpy: jasmine.SpyObj<MusicService>;
  let audioSpy: jasmine.SpyObj<AudioService>;
  let playbackSpy: jasmine.SpyObj<PlaybackSessionService>;
  let locationSpy: jasmine.SpyObj<Location>;
  let currentMusic: ReturnType<typeof signal<Music | null>>;
  let isPlaying: ReturnType<typeof signal<boolean>>;
  let currentTime: ReturnType<typeof signal<number>>;
  let duration: ReturnType<typeof signal<number>>;
  let volume: ReturnType<typeof signal<number>>;

  function setUp(opts: { music?: Music | null; playing?: boolean } = {}) {
    const music = 'music' in opts ? opts.music : makeTrack();
    currentMusic = signal<Music | null>(music ?? null);
    isPlaying = signal(opts.playing ?? false);
    currentTime = signal(60);
    duration = signal(240);
    volume = signal(0.6);

    musicSpy = jasmine.createSpyObj(
      'MusicService',
      ['selectMusic', 'isCurrentMusic'],
      {
        currentMusic,
        currentPlayList: signal<Playlist | null>(null),
        isLoading: signal(false),
        error: signal<string | null>(null),
      },
    );

    audioSpy = jasmine.createSpyObj(
      'AudioService',
      ['playToggle', 'seekTo', 'setVolume', 'loadMusic', 'cleanup'],
      { isPlaying, currentTime, duration, volume, ended: signal(false) },
    );

    playbackSpy = jasmine.createSpyObj(
      'PlaybackSessionService',
      [
        'next',
        'previous',
        'seek',
        'pause',
        'resume',
        'setRepeatMode',
        'setShuffle',
        'trackEnded',
      ],
      {
        session: signal(null),
        devices: signal([]),
        // Mirror the real service's behavior: when no session, displayed
        // position falls through to audioService.currentTime().
        displayedPositionMs: signal(0),
      },
    );

    locationSpy = jasmine.createSpyObj('Location', ['back']);

    TestBed.configureTestingModule({
      imports: [PlayerPageComponent],
      providers: [
        provideRouter([]),
        { provide: MusicService, useValue: musicSpy },
        { provide: AudioService, useValue: audioSpy },
        { provide: PlaybackSessionService, useValue: playbackSpy },
        { provide: Location, useValue: locationSpy },
      ],
    });

    fixture = TestBed.createComponent(PlayerPageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('renders the current track title and artist', () => {
    setUp({ music: makeTrack({ name: 'Loud Song', author: 'A Band' }) });
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Loud Song');
    expect(text).toContain('A Band');
  });

  it('renders the album art when imageRef is present', () => {
    setUp({ music: makeTrack({ imageRef: '/api/artwork/abc' }) });
    const img: HTMLImageElement = fixture.debugElement.query(
      By.css('.stage__art img'),
    ).nativeElement;
    expect(img.getAttribute('src')).toBe('/api/artwork/abc');
  });

  it('renders the idle empty state when no current track', () => {
    setUp({ music: null });
    expect(fixture.debugElement.query(By.css('.idle'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('.stage'))).toBeNull();
  });

  it('renders a DEVICES link in the top status bar', () => {
    setUp();
    const link = fixture.debugElement.query(By.css('.bar__devices'));
    expect(link).toBeTruthy();
    expect(link.nativeElement.getAttribute('href')).toBe('/devices');
  });

  it('shows PLAY when paused and PAUSE when playing', () => {
    setUp({ playing: false });
    const playBtn = fixture.debugElement.query(By.css('.transport__play'))
      .nativeElement as HTMLButtonElement;
    expect(playBtn.getAttribute('aria-label')).toBe('Play');

    isPlaying.set(true);
    fixture.detectChanges();
    expect(playBtn.getAttribute('aria-label')).toBe('Pause');
  });

  it('sends pause via the hub when the play button is clicked while playing', () => {
    setUp({ playing: true });
    fixture.debugElement
      .query(By.css('.transport__play'))
      .nativeElement.click();
    expect(playbackSpy.pause).toHaveBeenCalled();
    expect(audioSpy.playToggle).not.toHaveBeenCalled();
  });

  it('sends resume via the hub when the play button is clicked while paused', () => {
    setUp({ playing: false });
    fixture.debugElement
      .query(By.css('.transport__play'))
      .nativeElement.click();
    expect(playbackSpy.resume).toHaveBeenCalled();
  });

  it('sends previous via the hub when prev is clicked', () => {
    setUp();
    fixture.debugElement
      .query(By.css('.transport__prev'))
      .nativeElement.click();
    expect(playbackSpy.previous).toHaveBeenCalled();
  });

  it('sends next via the hub when next is clicked', () => {
    setUp();
    fixture.debugElement
      .query(By.css('.transport__next'))
      .nativeElement.click();
    expect(playbackSpy.next).toHaveBeenCalled();
  });

  it('routes onSeek through playbackSession.seek (in ms)', () => {
    setUp();
    const ev = new Event('input');
    const input = document.createElement('input');
    input.value = '83';
    Object.defineProperty(ev, 'target', { value: input });
    component.onSeek(ev);
    expect(playbackSpy.seek).toHaveBeenCalledWith(83_000);
    expect(audioSpy.seekTo).not.toHaveBeenCalled();
  });

  it('calls location.back when the close button is clicked', () => {
    setUp();
    fixture.debugElement
      .query(By.css('.bar__close'))
      .nativeElement.click();
    expect(locationSpy.back).toHaveBeenCalled();
  });

  it('toggleShuffle sends the inverse of the current server flag', () => {
    setUp();
    (playbackSpy.session as unknown as WritableSignal<any>).set({
      userId: 'u',
      activeDeviceId: null,
      trackId: null,
      positionMs: 0,
      isPlaying: false,
      queue: [],
      queueIndex: 0,
      repeatMode: 'off',
      shuffle: false,
      updatedAt: '2026-05-23T00:00:00Z',
    });
    fixture.detectChanges();
    component.toggleShuffle();
    expect(playbackSpy.setShuffle).toHaveBeenCalledWith(true);
  });

  it('cycleRepeat walks off → all → one → off', () => {
    setUp();
    const sessionSig = playbackSpy.session as unknown as WritableSignal<any>;
    const base = {
      userId: 'u',
      activeDeviceId: null,
      trackId: null,
      positionMs: 0,
      isPlaying: false,
      queue: [],
      queueIndex: 0,
      shuffle: false,
      updatedAt: '2026-05-23T00:00:00Z',
    };
    sessionSig.set({ ...base, repeatMode: 'off' });
    fixture.detectChanges();
    component.cycleRepeat();
    expect(playbackSpy.setRepeatMode).toHaveBeenCalledWith('all');

    sessionSig.set({ ...base, repeatMode: 'all' });
    fixture.detectChanges();
    component.cycleRepeat();
    expect(playbackSpy.setRepeatMode).toHaveBeenCalledWith('one');

    sessionSig.set({ ...base, repeatMode: 'one' });
    fixture.detectChanges();
    component.cycleRepeat();
    expect(playbackSpy.setRepeatMode).toHaveBeenCalledWith('off');
  });

  it('formats progress time as M:SS', () => {
    setUp();
    // displayedPositionMs is the source of truth for the rendered position.
    (playbackSpy.displayedPositionMs as unknown as WritableSignal<number>).set(83_000);
    currentTime.set(83);
    duration.set(240);
    fixture.detectChanges();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('1:23');
    expect(text).toContain('4:00');
  });
});
