import {
  Component,
  OnInit,
  computed,
  inject,
} from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs/operators';
import { ControllerService } from './features/player/services/controller.service';
import { PlaybackSessionService } from './features/player/services/playback-session.service';
import { MusicService } from './features/player/services/music.service';
import { PlayerComponent } from './features/player/components/player/player.component';

@Component({
  standalone: true,
  selector: 'app-root',
  imports: [RouterOutlet, PlayerComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent implements OnInit {
  protected readonly musicService = inject(MusicService);
  private readonly controllerService = inject(ControllerService);
  private readonly router = inject(Router);
  // Eagerly injected so the WS connects as soon as the user authenticates.
  private readonly _playbackSession = inject(PlaybackSessionService);

  private readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map((e) => e.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  protected readonly showMiniPlayer = computed(
    () => !!this.musicService.currentMusic() && this.currentUrl() !== '/player',
  );

  ngOnInit() {
    this.controllerService.setupControllerListeners();
  }
}
