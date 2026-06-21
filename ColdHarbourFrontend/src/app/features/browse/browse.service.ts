import { Injectable, signal } from '@angular/core';
import {
  ApiService,
  type AlbumDetail,
  type AlbumSummary,
  type ArtistDetail,
  type ArtistSummary,
} from '../../core/api/api.service';

/**
 * Read-side state for album & artist browsing. Subscribes to ApiService at the
 * boundary and exposes everything downstream as signals (RxJS dies here).
 */
@Injectable({ providedIn: 'root' })
export class BrowseService {
  readonly albums = signal<AlbumSummary[]>([]);
  readonly albumsLoading = signal(false);
  readonly albumsError = signal<string | null>(null);

  readonly album = signal<AlbumDetail | null>(null);
  readonly albumLoading = signal(false);
  readonly albumError = signal<string | null>(null);

  readonly artists = signal<ArtistSummary[]>([]);
  readonly artistsLoading = signal(false);
  readonly artistsError = signal<string | null>(null);

  readonly artist = signal<ArtistDetail | null>(null);
  readonly artistLoading = signal(false);
  readonly artistError = signal<string | null>(null);

  constructor(private api: ApiService) {}

  loadAlbums(): void {
    this.albumsLoading.set(true);
    this.albumsError.set(null);
    this.api.getAlbums().subscribe({
      next: (albums) => {
        this.albums.set(albums);
        this.albumsLoading.set(false);
      },
      error: () => {
        this.albumsError.set('Failed to load albums.');
        this.albumsLoading.set(false);
      },
    });
  }

  loadAlbum(id: string): void {
    this.albumLoading.set(true);
    this.albumError.set(null);
    this.album.set(null);
    this.api.getAlbum(id).subscribe({
      next: (album) => {
        this.album.set(album);
        this.albumLoading.set(false);
      },
      error: () => {
        this.albumError.set('Album not found.');
        this.albumLoading.set(false);
      },
    });
  }

  loadArtists(): void {
    this.artistsLoading.set(true);
    this.artistsError.set(null);
    this.api.getArtists().subscribe({
      next: (artists) => {
        this.artists.set(artists);
        this.artistsLoading.set(false);
      },
      error: () => {
        this.artistsError.set('Failed to load artists.');
        this.artistsLoading.set(false);
      },
    });
  }

  loadArtist(id: string): void {
    this.artistLoading.set(true);
    this.artistError.set(null);
    this.artist.set(null);
    this.api.getArtist(id).subscribe({
      next: (artist) => {
        this.artist.set(artist);
        this.artistLoading.set(false);
      },
      error: () => {
        this.artistError.set('Artist not found.');
        this.artistLoading.set(false);
      },
    });
  }
}
