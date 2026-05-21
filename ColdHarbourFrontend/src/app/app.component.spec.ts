import { TestBed } from '@angular/core/testing';
import { Component, signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { RouterOutlet, provideRouter } from '@angular/router';
import { AppComponent } from './app.component';
import { MusicService } from './features/player/services/music.service';
import { ControllerService } from './features/player/services/controller.service';
import { PlaybackSessionService } from './features/player/services/playback-session.service';
import type { Music, Playlist } from './core/api/api.service';

@Component({
  selector: 'app-player',
  standalone: true,
  template: `<div data-testid="player-stub"></div>`,
})
class StubPlayerComponent {}

describe('AppComponent', () => {
  let currentMusic: ReturnType<typeof signal<Music | null>>;

  beforeEach(async () => {
    currentMusic = signal<Music | null>(null);

    const musicSpy = jasmine.createSpyObj(
      'MusicService',
      ['setCurrentPlaylist', 'selectMusic', 'nextMusic', 'previousMusic', 'isCurrentMusic'],
      {
        currentMusic,
        currentPlayList: signal<Playlist | null>(null),
        isLoading: signal(false),
        error: signal<string | null>(null),
      },
    );

    const controllerSpy = jasmine.createSpyObj('ControllerService', [
      'setupControllerListeners',
    ]);

    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: MusicService, useValue: musicSpy },
        { provide: ControllerService, useValue: controllerSpy },
        { provide: PlaybackSessionService, useValue: {} },
      ],
    })
      .overrideComponent(AppComponent, {
        set: { imports: [RouterOutlet, StubPlayerComponent] },
      })
      .compileComponents();
  });

  it('creates the app', () => {
    const fixture = TestBed.createComponent(AppComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('hides the global player when no track is selected', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('app-player'))).toBeNull();
  });

  it('renders the global player when a track is selected', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    currentMusic.set({
      id: 1,
      trackId: 't',
      albumId: 'a',
      name: 'Song',
      author: 'Artist',
      audioRef: '/api/stream/t',
      imageRef: '/api/artwork/a',
      durationSeconds: 200,
    });
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('app-player'))).toBeTruthy();
  });
});
