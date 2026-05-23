import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { Location } from '@angular/common';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { PlayerPageComponent } from './player-page.component';
import { MusicService } from '../../services/music.service';
import { AudioService } from '../../services/audio.service';
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
      ['selectMusic', 'nextMusic', 'previousMusic', 'isCurrentMusic'],
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

    locationSpy = jasmine.createSpyObj('Location', ['back']);

    TestBed.configureTestingModule({
      imports: [PlayerPageComponent],
      providers: [
        provideRouter([]),
        { provide: MusicService, useValue: musicSpy },
        { provide: AudioService, useValue: audioSpy },
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
      By.css('.player-page__art img'),
    ).nativeElement;
    expect(img.getAttribute('src')).toBe('/api/artwork/abc');
  });

  it('renders the idle empty state when no current track', () => {
    setUp({ music: null });
    expect(fixture.debugElement.query(By.css('.player-page__idle'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('.player-page__art'))).toBeNull();
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

  it('toggles playback when the play button is clicked', () => {
    setUp();
    const playBtn = fixture.debugElement.query(By.css('.transport__play'))
      .nativeElement as HTMLButtonElement;
    playBtn.click();
    expect(audioSpy.playToggle).toHaveBeenCalled();
  });

  it('calls musicService.previousMusic when prev is clicked', () => {
    setUp();
    fixture.debugElement
      .query(By.css('.transport__prev'))
      .nativeElement.click();
    expect(musicSpy.previousMusic).toHaveBeenCalled();
  });

  it('calls musicService.nextMusic when next is clicked', () => {
    setUp();
    fixture.debugElement
      .query(By.css('.transport__next'))
      .nativeElement.click();
    expect(musicSpy.nextMusic).toHaveBeenCalled();
  });

  it('calls location.back when the close button is clicked', () => {
    setUp();
    fixture.debugElement
      .query(By.css('.player-page__close'))
      .nativeElement.click();
    expect(locationSpy.back).toHaveBeenCalled();
  });

  it('formats progress time as M:SS', () => {
    setUp();
    currentTime.set(83);
    duration.set(240);
    fixture.detectChanges();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('1:23');
    expect(text).toContain('4:00');
  });
});
