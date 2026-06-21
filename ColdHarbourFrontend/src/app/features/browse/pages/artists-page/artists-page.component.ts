import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { BackButtonComponent } from '../../../../shared/ui';
import { BrowseService } from '../../browse.service';

@Component({
  selector: 'app-artists-page',
  standalone: true,
  imports: [RouterLink, BackButtonComponent],
  templateUrl: './artists-page.component.html',
  styleUrl: './artists-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ArtistsPageComponent implements OnInit {
  protected readonly browse = inject(BrowseService);

  ngOnInit(): void {
    this.browse.loadArtists();
  }

  meta(albumCount: number): string {
    return `${albumCount} ${albumCount === 1 ? 'ALBUM' : 'ALBUMS'}`;
  }
}
