import { Component, OnInit, Signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MusicListComponent } from '../../components/music-list/music-list.component';
import { ActivatedRoute } from '@angular/router';
import { MusicService } from '../../../player/services/music.service';
import type { Playlist } from '../../../../core/api/api.service';
import { LibraryService } from '../../library.service';
import {
  ButtonComponent,
  ModalComponent,
} from '../../../../shared/ui';

@Component({
  selector: 'app-playlist-page',
  imports: [
    MusicListComponent,
    RouterLink,
    ButtonComponent,
    ModalComponent,
  ],
  templateUrl: './playlist-page.component.html',
  styleUrl: './playlist-page.component.scss',
})
export class PlaylistPageComponent implements OnInit {
  public currentPlayList: Signal<Playlist | null>;

  constructor(
    private route: ActivatedRoute,
    private musicService: MusicService,
    public libraryService: LibraryService,
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
    Array.from(input.files).forEach((file) =>
      this.libraryService.uploadFile(file),
    );
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
