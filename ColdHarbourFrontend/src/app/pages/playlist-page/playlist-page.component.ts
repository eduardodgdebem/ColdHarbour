import { Component, OnInit, Signal } from '@angular/core';
import { MusicListComponent } from '../../components/music-list/music-list.component';
import { PlayerComponent } from '../../components/player/player.component';
import { ActivatedRoute } from '@angular/router';
import { MusicService, Playlist } from '../../services/music.service';
import { LibraryService } from '../../services/library.service';


@Component({
  selector: 'app-playlist-page',
  imports: [MusicListComponent, PlayerComponent],
  templateUrl: './playlist-page.component.html',
  styleUrl: './playlist-page.component.scss',
})
export class PlaylistPageComponent implements OnInit {
  public currentPlayList: Signal<Playlist | null>;

  constructor(
    private route: ActivatedRoute,
    private musicService: MusicService,
    public libraryService: LibraryService
  ) {
    this.currentPlayList = musicService.currentPlayList;
  }

  ngOnInit(): void {
    this.route.params.subscribe((params) => {
      this.musicService.setCurrentPlaylist(params['id']);
    });
  }

  onFilesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!input.files?.length) return;
    Array.from(input.files).forEach(file => this.libraryService.uploadFile(file));
    input.value = '';
  }

  previewSync(): void {
    this.libraryService.previewSync();
  }

  applySync(): void {
    this.libraryService.applySync();
  }

  closeSync(): void {
    this.libraryService.syncDiff.set(null);
  }
}
