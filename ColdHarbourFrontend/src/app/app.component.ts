import { Component, OnInit, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
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
  // Eagerly injected so the WS connects as soon as the user authenticates.
  private readonly _playbackSession = inject(PlaybackSessionService);

  ngOnInit() {
    this.controllerService.setupControllerListeners();
  }
}
