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
import { BackButtonComponent, ButtonComponent } from '../../../../shared/ui';
import { MusicListComponent } from '../../../library/components/music-list/music-list.component';
import type { Music } from '../../../../core/api/api.service';
import { BrowseService } from '../../browse.service';
import {
  EditAlbumModalComponent,
  type EditAlbumPayload,
} from '../../components/edit-album-modal/edit-album-modal.component';
import {
  EditSongModalComponent,
  type EditSongPayload,
} from '../../components/edit-song-modal/edit-song-modal.component';

@Component({
  selector: 'app-album-detail-page',
  standalone: true,
  imports: [
    RouterLink,
    BackButtonComponent,
    ButtonComponent,
    MusicListComponent,
    EditAlbumModalComponent,
    EditSongModalComponent,
  ],
  templateUrl: './album-detail-page.component.html',
  styleUrl: './album-detail-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AlbumDetailPageComponent implements OnInit {
  protected readonly browse = inject(BrowseService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly coverError = signal(false);
  protected readonly albumEditOpen = signal(false);
  protected readonly songEditTarget = signal<Music | null>(null);

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

  protected openAlbumEdit(): void {
    this.albumEditOpen.set(true);
  }

  protected onAlbumSave(payload: EditAlbumPayload): void {
    const album = this.browse.album();
    if (!album) return;
    this.browse.saveAlbum(
      album.id,
      { title: payload.title, year: payload.year },
      payload.coverFile,
      () => this.albumEditOpen.set(false),
    );
  }

  protected openSongEdit(music: Music): void {
    this.songEditTarget.set(music);
  }

  protected onSongSave(payload: EditSongPayload): void {
    const album = this.browse.album();
    const target = this.songEditTarget();
    if (!album || !target) return;
    this.browse.saveTrack(
      album.id,
      target.trackId,
      { title: payload.title, trackNumber: payload.trackNumber },
      () => this.songEditTarget.set(null),
    );
  }
}
