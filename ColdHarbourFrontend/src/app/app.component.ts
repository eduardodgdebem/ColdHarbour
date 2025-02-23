import { Component, OnInit } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ApiService } from './services/api.service';
import { HttpClient } from '@angular/common/http';
import { PlayerComponent } from './components/player/player.component';

@Component({
  standalone: true,
  selector: 'app-root',
  imports: [RouterOutlet, PlayerComponent],
  providers: [ApiService],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent implements OnInit {
  title = 'CodlHarbourFrontEnd';

  constructor(private apiService: ApiService) { }

  ngOnInit() {
    this.apiService.getWeatherForecast().subscribe(console.log);
  }
}
