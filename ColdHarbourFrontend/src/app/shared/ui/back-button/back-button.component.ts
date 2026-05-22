import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
} from '@angular/core';
import { Location } from '@angular/common';

export type BackButtonVariant = 'default' | 'inverse';

@Component({
  selector: 'app-back-button',
  standalone: true,
  templateUrl: './back-button.component.html',
  styleUrl: './back-button.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BackButtonComponent {
  /** Label after the arrow; rendered uppercase. */
  readonly label = input<string>('Back');
  /** `default` for light backgrounds; `inverse` for dark headers. */
  readonly variant = input<BackButtonVariant>('default');

  private readonly location = inject(Location);

  protected readonly classes = computed(() => [
    'back-btn',
    `back-btn--${this.variant()}`,
  ]);

  protected readonly displayLabel = computed(
    () => `← ${this.label().toUpperCase()}`,
  );

  back(): void {
    this.location.back();
  }
}
