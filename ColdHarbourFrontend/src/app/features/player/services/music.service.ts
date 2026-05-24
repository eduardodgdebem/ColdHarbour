import { effect, Injectable, signal } from '@angular/core';
import { ApiService, Music, Playlist } from '../../../core/api/api.service';
import { AudioService } from './audio.service';
import { ColorService } from './color.service';

export type { Music, Playlist };

@Injectable({
  providedIn: 'root',
})
export class MusicService {
  public currentMusic = signal<Music | null>(null);
  public currentPlayList = signal<Playlist | null>(null);
  public isLoading = signal<boolean>(true);
  public error = signal<string | null>(null);
  public currentMusicIndex = signal<number>(0);

  constructor(
    private apiService: ApiService,
    private audioService: AudioService,
    private colorService: ColorService,
  ) {
    // Color extraction reacts to image changes.
    effect(() => {
      const music = this.currentMusic();
      if (music?.imageRef) {
        this.colorService.extractColor(music.imageRef);
      }
    });

    // Audio loading lives at the service level (not on PlayerComponent),
    // so it survives PlayerComponent unmount-on-/player and doesn't fire
    // spuriously on remount. Loads the source whenever audioRef changes.
    effect(() => {
      const music = this.currentMusic();
      if (music?.audioRef) {
        this.audioService.loadMusic(music.audioRef);
      }
    });
  }

  public setCurrentPlaylist(id: number) {
    this.currentPlayList.set(null);
    this.loadPlaylist(id);
  }

  private loadPlaylist(id: number) {
    this.isLoading.set(true);
    this.error.set(null);

    this.apiService.getPlaylist(id).subscribe({
      next: (playlist) => {
        this.currentPlayList.set(playlist);
        this.isLoading.set(false);
      },
      error: (err) => {
        console.error('Error loading playlist:', err);
        this.error.set('Failed to load playlist');
        this.isLoading.set(false);
      },
    });
  }

  public selectMusic(music: Music) {
    this.currentMusic.set(music);
    const playlist = this.currentPlayList();
    if (playlist) {
      const idx = playlist.musics.findIndex((m) => m.trackId === music.trackId);
      if (idx >= 0) this.currentMusicIndex.set(idx);
    }
  }

  public isCurrentMusic(music: Music): boolean {
    return this.currentMusic()?.id === music.id;
  }
}
