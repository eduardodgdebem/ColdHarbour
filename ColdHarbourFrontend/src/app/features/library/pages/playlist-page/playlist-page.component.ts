import { Component, OnInit, Signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MusicListComponent } from '../../components/music-list/music-list.component';
import { LibraryActionsComponent } from '../../components/library-actions/library-actions.component';
import { ActivatedRoute } from '@angular/router';
import { MusicService } from '../../../player/services/music.service';
import type { Playlist } from '../../../../core/api/api.service';
import {
  BackButtonComponent,
  ButtonComponent,
} from '../../../../shared/ui';

@Component({
  selector: 'app-playlist-page',
  imports: [
    MusicListComponent,
    LibraryActionsComponent,
    RouterLink,
    BackButtonComponent,
    ButtonComponent,
  ],
  templateUrl: './playlist-page.component.html',
  styleUrl: './playlist-page.component.scss',
})
export class PlaylistPageComponent implements OnInit {
  public currentPlayList: Signal<Playlist | null>;

  constructor(
    private route: ActivatedRoute,
    private musicService: MusicService,
  ) {
    this.currentPlayList = musicService.currentPlayList;
  }

  ngOnInit(): void {
    this.route.params.subscribe((params) => {
      this.musicService.setCurrentPlaylist(params['id']);
    });
  }
}
