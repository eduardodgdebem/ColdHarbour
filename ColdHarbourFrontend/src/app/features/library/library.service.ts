import { Injectable, signal } from '@angular/core';
import { ApiService, LibrarySyncDiff } from '../../core/api/api.service';
import { MusicService } from '../player/services/music.service';

@Injectable({ providedIn: 'root' })
export class LibraryService {
  public isUploading = signal(false);
  public uploadError = signal<string | null>(null);
  public isSyncing = signal(false);
  public syncDiff = signal<LibrarySyncDiff | null>(null);
  public syncError = signal<string | null>(null);

  constructor(
    private api: ApiService,
    private musicService: MusicService,
  ) {}

  uploadFile(file: File): void {
    this.isUploading.set(true);
    this.uploadError.set(null);

    this.api.uploadTrack(file).subscribe({
      next: () => {
        this.isUploading.set(false);
        this.musicService.setCurrentPlaylist(1); // refresh playlist
      },
      error: (err) => {
        console.error('Upload failed', err);
        this.uploadError.set('Upload failed. Check file format.');
        this.isUploading.set(false);
      },
    });
  }

  deleteTrack(trackId: string): void {
    this.api.deleteTrack(trackId).subscribe({
      next: () => this.musicService.setCurrentPlaylist(1),
      error: (err) => console.error('Delete failed', err),
    });
  }

  previewSync(): void {
    this.syncError.set(null);
    this.api.previewSync().subscribe({
      next: (diff) => this.syncDiff.set(diff),
      error: (err) => {
        console.error('Sync preview failed', err);
        this.syncError.set('Sync preview failed.');
      },
    });
  }

  applySync(): void {
    this.isSyncing.set(true);
    this.syncDiff.set(null);
    this.api.applySync().subscribe({
      next: () => {
        this.isSyncing.set(false);
        this.musicService.setCurrentPlaylist(1);
      },
      error: (err) => {
        console.error('Sync apply failed', err);
        this.syncError.set('Sync apply failed.');
        this.isSyncing.set(false);
      },
    });
  }
}
