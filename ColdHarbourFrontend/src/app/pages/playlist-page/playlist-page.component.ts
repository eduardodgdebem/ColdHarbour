import { Component, OnInit, Signal } from '@angular/core';
import { MusicListComponent } from '../../components/music-list/music-list.component';
import { PlayerComponent } from '../../components/player/player.component';
import { ActivatedRoute } from '@angular/router';
import { MusicService, Playlist } from '../../services/music.service';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-playlist-page',
  imports: [CommonModule ,MusicListComponent, PlayerComponent],
  templateUrl: './playlist-page.component.html',
  styleUrl: './playlist-page.component.scss',
})
export class PlaylistPageComponent implements OnInit {
  public currentPlayList: Signal<Playlist | null>;

  constructor(
    private route: ActivatedRoute,
    private musicService: MusicService
  ) {
    this.currentPlayList = musicService.currentPlayList;
  }

  ngOnInit(): void {
    this.route.params.subscribe((params) => {
      this.musicService.setCurrentPlaylist(params['id']);
    });
  }
}
