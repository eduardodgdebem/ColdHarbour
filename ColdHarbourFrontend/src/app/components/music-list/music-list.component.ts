import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MusicService } from '../../services/music.service';
import type { Music } from '../../services/music.service';

@Component({
  selector: 'app-music-list',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './music-list.component.html',
  styleUrl: './music-list.component.scss'
})
export class MusicListComponent {
  constructor(public musicService: MusicService) { }

  selectMusic(music: Music) {
    this.musicService.selectMusic(music);
  }

  isCurrentMusic(music: Music): boolean {
    return this.musicService.isCurrentMusic(music);
  }
}
