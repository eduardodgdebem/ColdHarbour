import { Component, effect, OnInit } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { CommonModule } from '@angular/common';
import { ControllerService } from './services/controller.service';
@Component({
  standalone: true,
  selector: 'app-root',
  imports: [RouterOutlet, CommonModule],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent {
  constructor(private controllerService: ControllerService) {}

  ngOnInit() {
    this.controllerService.addKeyListener();
  }
}
