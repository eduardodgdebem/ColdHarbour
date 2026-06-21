import {
  ChangeDetectionStrategy,
  Component,
  input,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Brutalist cover tile used for browsing albums (and album-shaped collections).
 * Decoupled from API types — takes primitive inputs so it stays free of any
 * `core/*` or `features/*` dependency.
 */
@Component({
  selector: 'app-album-card',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './album-card.component.html',
  styleUrl: './album-card.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AlbumCardComponent {
  readonly title = input.required<string>();
  readonly subtitle = input<string>('');
  readonly meta = input<string>('');
  readonly coverRef = input<string>('');
  readonly link = input.required<string | unknown[]>();

  protected readonly imageError = signal(false);

  onImageError(): void {
    this.imageError.set(true);
  }
}
