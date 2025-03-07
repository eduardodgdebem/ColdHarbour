import { Component, effect, OnInit } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ApiService } from './services/api.service';
import { HttpClient } from '@angular/common/http';
import { PlayerComponent } from './components/player/player.component';
import { MusicListComponent } from './components/music-list/music-list.component';
import { MusicService } from './services/music.service';
import { ColorService } from './services/color.service';
import { CommonModule } from '@angular/common';

@Component({
  standalone: true,
  selector: 'app-root',
  imports: [RouterOutlet, PlayerComponent, MusicListComponent, CommonModule],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent implements OnInit {
  public backgroundImg?: string;

  constructor() {}

  ngOnInit() {}
}
