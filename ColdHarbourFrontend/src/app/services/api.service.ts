import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, map } from 'rxjs';
import type { Music } from './music.service';

@Injectable({
  providedIn: 'root',
})
export class ApiService {
  // In a production environment, this would come from environment configuration
  private readonly API_URL = 'http://localhost:8080/api';
  private readonly ASSETS_URL = 'http://localhost:8080';

  constructor(private http: HttpClient) { }

  getPlaylist(): Observable<Music[]> {
    return this.http.get<Music[]>(`${this.API_URL}/music/playlist`).pipe(
      map(playlist => playlist.map(music => ({
        ...music,
        audioRef: `${this.ASSETS_URL}${music.audioRef}`,
        imageRef: `${this.ASSETS_URL}${music.imageRef}`
      })))
    );
  }
}
