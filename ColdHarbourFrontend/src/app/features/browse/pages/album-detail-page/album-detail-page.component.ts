import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { BackButtonComponent } from '../../../../shared/ui';
import { MusicListComponent } from '../../../library/components/music-list/music-list.component';
import { BrowseService } from '../../browse.service';

@Component({
  selector: 'app-album-detail-page',
  standalone: true,
  imports: [RouterLink, BackButtonComponent, MusicListComponent],
  templateUrl: './album-detail-page.component.html',
  styleUrl: './album-detail-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AlbumDetailPageComponent implements OnInit {
  protected readonly browse = inject(BrowseService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly coverError = signal(false);

  ngOnInit(): void {
    this.route.paramMap
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((params) => {
        const id = params.get('id');
        if (id) this.browse.loadAlbum(id);
      });
  }

  yearLabel(year: number | null): string {
    return year ? `${year}` : '—';
  }
}
