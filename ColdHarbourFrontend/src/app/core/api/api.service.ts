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
  // Always present from the API; optional in the type so existing test fixtures
  // need not be retrofitted.
  trackNumber?: number | null;
};

export type Playlist = {
  name: string;
  imageRef: string;
  id: number;
  musics: Music[];
};

export type AlbumSummary = {
  id: string;
  title: string;
  artist: string;
  artistId: string;
  year: number | null;
  imageRef: string;
  trackCount: number;
};

export type AlbumDetail = {
  id: string;
  title: string;
  artist: string;
  artistId: string;
  year: number | null;
  imageRef: string;
  tracks: Music[];
};

export type ArtistSummary = {
  id: string;
  name: string;
  albumCount: number;
};

export type ArtistDetail = {
  id: string;
  name: string;
  albums: AlbumSummary[];
};

export type LibrarySyncItem = {
  path: string;
  title: string | null;
  artist: string | null;
};
export type LibrarySyncDiff = {
  added: LibrarySyncItem[];
  missing: LibrarySyncItem[];
  renamed: LibrarySyncItem[];
};
export type UploadResult = {
  trackId: string;
  albumId: string;
  alreadyExisted: boolean;
};

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
      })),
    );
  }

  getAlbums(): Observable<AlbumSummary[]> {
    return this.http
      .get<AlbumSummary[]>(`${this.API_URL}/albums`)
      .pipe(map((albums) => albums.map((a) => this.withAlbumImage(a))));
  }

  getAlbum(id: string): Observable<AlbumDetail> {
    return this.http.get<AlbumDetail>(`${this.API_URL}/albums/${id}`).pipe(
      map((album) => ({
        ...album,
        imageRef: `${this.ASSETS_URL}${album.imageRef}`,
        tracks: album.tracks.map((music) => ({
          ...music,
          audioRef: `${this.ASSETS_URL}${music.audioRef}`,
          imageRef: `${this.ASSETS_URL}${music.imageRef}`,
        })),
      })),
    );
  }

  getArtists(): Observable<ArtistSummary[]> {
    return this.http.get<ArtistSummary[]>(`${this.API_URL}/artists`);
  }

  getArtist(id: string): Observable<ArtistDetail> {
    return this.http.get<ArtistDetail>(`${this.API_URL}/artists/${id}`).pipe(
      map((artist) => ({
        ...artist,
        albums: artist.albums.map((a) => this.withAlbumImage(a)),
      })),
    );
  }

  private withAlbumImage(album: AlbumSummary): AlbumSummary {
    return { ...album, imageRef: `${this.ASSETS_URL}${album.imageRef}` };
  }

  updateTrack(
    id: string,
    body: { title: string; trackNumber: number | null },
  ): Observable<void> {
    return this.http.patch<void>(`${this.API_URL}/tracks/${id}`, body);
  }

  updateAlbum(
    id: string,
    body: { title: string; year: number | null },
  ): Observable<void> {
    return this.http.patch<void>(`${this.API_URL}/albums/${id}`, body);
  }

  renameArtist(id: string, body: { name: string }): Observable<void> {
    return this.http.patch<void>(`${this.API_URL}/artists/${id}`, body);
  }

  uploadAlbumCover(id: string, file: File): Observable<void> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<void>(`${this.API_URL}/albums/${id}/cover`, form);
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
    return this.http.get<LibrarySyncDiff>(
      `${this.API_URL}/library/sync/preview`,
    );
  }

  applySync(): Observable<void> {
    return this.http.post<void>(`${this.API_URL}/library/sync`, null);
  }
}
