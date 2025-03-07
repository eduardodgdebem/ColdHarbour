import { effect, Injectable, signal } from '@angular/core';
import { ApiService } from './api.service';
import { ColorService } from './color.service';

export type Music = {
  name: string;
  author: string;
  audioRef: string;
  imageRef: string;
  id: number;
};

@Injectable({
  providedIn: 'root',
})
export class MusicService {
  public currentMusic = signal<Music | null>(null);
  public currentPlayList = signal<Music[]>([]);
  public isLoading = signal<boolean>(true);
  public error = signal<string | null>(null);

  constructor(
    private apiService: ApiService,
    private colorService: ColorService
  ) {
    this.loadPlaylist();

    effect(() => {
      const music = this.currentMusic();
      if (music?.imageRef) {
        const encodedImageRef = music.imageRef.replace(/ /g, '%20');
        this.colorService.extractColor(encodedImageRef);
      }
    });
  }

  private loadPlaylist() {
    this.isLoading.set(true);
    this.error.set(null);

    this.apiService.getPlaylist().subscribe({
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

  selectMusic(music: Music) {
    this.currentMusic.set(music);
  }

  isCurrentMusic(music: Music): boolean {
    return this.currentMusic()?.id === music.id;
  }
}
