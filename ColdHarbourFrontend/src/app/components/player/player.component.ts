import { AfterViewInit, Component, effect, ElementRef, signal, ViewChild } from '@angular/core';
import { AudioService } from '../../services/audio.service';

@Component({
  selector: 'app-player',
  imports: [],
  providers: [AudioService],
  templateUrl: './player.component.html',
  styleUrl: './player.component.scss'
})
export class PlayerComponent {
  private musicRef = "/Baby You're Bad - HONNE.mp3"

  constructor(public audioService: AudioService) {
    this.audioService.loadMusic(this.musicRef);
  }

  public mainButtonClick() {
    this.audioService.playToggle();
  }

  public onInputChange(e: Event) {
    console.log(e);
  }
}
