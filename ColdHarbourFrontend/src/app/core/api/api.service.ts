import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../../environments/environment';

export type Music = {
  id: number;
  trackId: string;
  albumId: string;
  name: string;
  author: string;
  audioRef: string;
  imageRef: string;
  durationSeconds: number;
};

export type Playlist = {
  name: string;
  imageRef: string;
  id: number;
  musics: Music[];
};

export type LibrarySyncItem = { path: string; title: string | null; artist: string | null };
export type LibrarySyncDiff = {
  added: LibrarySyncItem[];
  missing: LibrarySyncItem[];
  renamed: LibrarySyncItem[];
};
export type UploadResult = { trackId: string; albumId: string; alreadyExisted: boolean };

@Injectable({
  providedIn: 'root',
})
export class ApiService {
  private readonly API_URL = environment.apiBase;
  private readonly ASSETS_URL = environment.assetsBase;

  constructor(private http: HttpClient) {}

  getPlaylist(id: number): Observable<Playlist> {
    return this.http.get<Playlist>(`${this.API_URL}/music/playlist/${id}`).pipe(
      map((playlist) => ({
        ...playlist,
        musics: playlist.musics.map((music: Music) => ({
          ...music,
          audioRef: `${this.ASSETS_URL}${music.audioRef}`,
          imageRef: `${this.ASSETS_URL}${music.imageRef}`,
        })),
      }))
    );
  }

  uploadTrack(file: File): Observable<UploadResult> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<UploadResult>(`${this.API_URL}/library/tracks`, form);
  }

  deleteTrack(trackId: string): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/library/tracks/${trackId}`);
  }

  previewSync(): Observable<LibrarySyncDiff> {
    return this.http.get<LibrarySyncDiff>(`${this.API_URL}/library/sync/preview`);
  }

  applySync(): Observable<void> {
    return this.http.post<void>(`${this.API_URL}/library/sync`, null);
  }
}
