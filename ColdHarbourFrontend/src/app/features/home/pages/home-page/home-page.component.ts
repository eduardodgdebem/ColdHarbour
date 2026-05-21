import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { MusicService } from '../../../player/services/music.service';
import { AuthService } from '../../../../core/auth/auth.service';
import type { Music } from '../../../../core/api/api.service';
import { ButtonComponent } from '../../../../shared/ui';

@Component({
  selector: 'app-home-page',
  standalone: true,
  imports: [RouterLink, ButtonComponent],
  templateUrl: './home-page.component.html',
  styleUrl: './home-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomePageComponent implements OnInit {
  private readonly musicService = inject(MusicService);
  private readonly authService = inject(AuthService);

  readonly isLoading = this.musicService.isLoading;
  readonly playlist = this.musicService.currentPlayList;

  readonly trackCount = computed(
    () => this.playlist()?.musics.length ?? 0,
  );

  readonly totalDuration = computed(() => {
    const musics = this.playlist()?.musics ?? [];
    const total = musics.reduce((sum, m) => sum + m.durationSeconds, 0);
    return this.formatTotalDuration(total);
  });

  readonly albumCount = computed(() => {
    const musics = this.playlist()?.musics ?? [];
    return new Set(musics.map((m) => m.albumId)).size;
  });

  readonly recentlyAdded = computed<Music[]>(() => {
    const musics = this.playlist()?.musics ?? [];
    return musics.slice(-8).reverse();
  });

  readonly isEmpty = computed(
    () => !this.isLoading() && this.trackCount() === 0,
  );

  readonly userName = computed(() => {
    const name = this.authService.name();
    if (name && name.trim()) {
      return name.trim().split(/\s+/)[0].toUpperCase();
    }
    const email = this.authService.email();
    if (email) {
      return email.split('@')[0].split(/[._-]/)[0].toUpperCase();
    }
    return 'FRIEND';
  });

  readonly nowLabel = this.computeNowLabel();

  ngOnInit(): void {
    this.musicService.setCurrentPlaylist(1);
  }

  play(track: Music): void {
    this.musicService.selectMusic(track);
  }

  formatDuration(seconds: number): string {
    const m = Math.floor(seconds / 60);
    const s = Math.floor(seconds % 60);
    return `${m}:${s.toString().padStart(2, '0')}`;
  }

  private formatTotalDuration(totalSeconds: number): string {
    const totalMinutes = Math.floor(totalSeconds / 60);
    const hours = Math.floor(totalMinutes / 60);
    const minutes = totalMinutes % 60;
    if (hours === 0) return `${minutes}M`;
    return `${hours}H ${minutes.toString().padStart(2, '0')}M`;
  }

  private computeNowLabel(): string {
    const now = new Date();
    const months = [
      'JAN', 'FEB', 'MAR', 'APR', 'MAY', 'JUN',
      'JUL', 'AUG', 'SEP', 'OCT', 'NOV', 'DEC',
    ];
    const day = now.getDate().toString().padStart(2, '0');
    const mon = months[now.getMonth()];
    const year = now.getFullYear();
    const hh = now.getHours().toString().padStart(2, '0');
    const mm = now.getMinutes().toString().padStart(2, '0');
    return `${day} ${mon} ${year} · ${hh}:${mm}`;
  }
}
