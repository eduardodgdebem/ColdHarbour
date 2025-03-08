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

export type Playlist = {
  name: string;
  imageRef: string;
  id: number;
  musics: Music[];
};

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
    private colorService: ColorService
  ) {
    effect(() => {
      const music = this.currentMusic();
      if (music?.imageRef) {
        const encodedImageRef = music.imageRef.replace(/ /g, '%20');
        this.colorService.extractColor(encodedImageRef);
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
  }

  public isCurrentMusic(music: Music): boolean {
    return this.currentMusic()?.id === music.id;
  }

  public nextMusic() {
    const currentMusic = this.currentMusic();
    if (currentMusic) {
      let nextIndex = this.currentMusicIndex() + 1;
      if (nextIndex >= (this.currentPlayList()?.musics?.length ?? 1)) {
        nextIndex = 0;
      }
      const nextMusic = this.currentPlayList()?.musics[nextIndex];
      if (nextMusic) {
        this.selectMusic(nextMusic);
        this.currentMusicIndex.set(nextIndex);
      }
    }
  }

  public previousMusic() {
    const currentMusic = this.currentMusic();
    if (currentMusic) {
      let previousIndex = this.currentMusicIndex() - 1;
      if (previousIndex < 0) {
        previousIndex = this.currentPlayList()?.musics?.length ?? 1;
      }
      const previousMusic = this.currentPlayList()?.musics[previousIndex];
      if (previousMusic) {
        this.selectMusic(previousMusic);
        this.currentMusicIndex.set(previousIndex);
      }
    }
  }
}
