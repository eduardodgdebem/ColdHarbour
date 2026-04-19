import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, map } from 'rxjs';
import type { Playlist } from './music.service';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root',
})
export class ApiService {
  private readonly API_URL = environment.apiBase;
  private readonly ASSETS_URL = environment.assetsBase;

  constructor(private http: HttpClient) {}

  getPlaylist(id: number): Observable<Playlist> {
    return this.http.get<Playlist>(`${this.API_URL}/music/playlist/${id}`).pipe(
      map((playlist) => {
        return {
          ...playlist,
          imageRef: `${this.ASSETS_URL}${playlist.imageRef}`,
          musics: playlist.musics.map((music) => ({
            ...music,
            audioRef: `${this.ASSETS_URL}${music.audioRef}`,
            imageRef: `${this.ASSETS_URL}${music.imageRef}`,
          })),
        };
      })
    );
  }
}
