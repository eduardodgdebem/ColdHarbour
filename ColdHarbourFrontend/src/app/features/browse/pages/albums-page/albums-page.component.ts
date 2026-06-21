import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
} from '@angular/core';
import { AlbumCardComponent, BackButtonComponent } from '../../../../shared/ui';
import { BrowseService } from '../../browse.service';

@Component({
  selector: 'app-albums-page',
  standalone: true,
  imports: [AlbumCardComponent, BackButtonComponent],
  templateUrl: './albums-page.component.html',
  styleUrl: './albums-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AlbumsPageComponent implements OnInit {
  protected readonly browse = inject(BrowseService);

  ngOnInit(): void {
    this.browse.loadAlbums();
  }

  meta(trackCount: number): string {
    return `${trackCount} ${trackCount === 1 ? 'TRACK' : 'TRACKS'}`;
  }
}
