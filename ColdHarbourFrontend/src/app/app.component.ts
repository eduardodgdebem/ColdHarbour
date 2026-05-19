import { Component, effect, OnInit } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { ControllerService } from './features/player/services/controller.service';
@Component({
  standalone: true,
    selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent {
  constructor(private controllerService: ControllerService) {}

  ngOnInit() {
    this.controllerService.setupControllerListeners();  }
}
