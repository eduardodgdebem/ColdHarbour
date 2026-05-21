import {
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
} from '@angular/core';

export type BadgeVariant = 'default' | 'active' | 'accent';

@Component({
  selector: 'app-badge',
  standalone: true,
  templateUrl: './badge.component.html',
  styleUrl: './badge.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BadgeComponent {
  variant = input<BadgeVariant>('default');

  protected readonly classes = computed(() => [
    'badge',
    `badge--${this.variant()}`,
  ]);
}
