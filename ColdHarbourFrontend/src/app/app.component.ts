import { Component, OnInit } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ControllerService } from './features/player/services/controller.service';
import { PlaybackSessionService } from './features/player/services/playback-session.service';

@Component({
  standalone: true,
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent {
  constructor(
    private controllerService: ControllerService,
    // Eagerly injected so the WS connects as soon as the user authenticates.
    private _playbackSession: PlaybackSessionService,
  ) {}

  ngOnInit() {
    this.controllerService.setupControllerListeners();
  }
}
