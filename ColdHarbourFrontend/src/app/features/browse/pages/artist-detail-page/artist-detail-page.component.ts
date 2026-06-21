import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  inject,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { AlbumCardComponent, BackButtonComponent } from '../../../../shared/ui';
import { BrowseService } from '../../browse.service';

@Component({
  selector: 'app-artist-detail-page',
  standalone: true,
  imports: [AlbumCardComponent, BackButtonComponent],
  templateUrl: './artist-detail-page.component.html',
  styleUrl: './artist-detail-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ArtistDetailPageComponent implements OnInit {
  protected readonly browse = inject(BrowseService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  ngOnInit(): void {
    this.route.paramMap
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((params) => {
        const id = params.get('id');
        if (id) this.browse.loadArtist(id);
      });
  }

  meta(trackCount: number): string {
    return `${trackCount} ${trackCount === 1 ? 'TRACK' : 'TRACKS'}`;
  }
}
