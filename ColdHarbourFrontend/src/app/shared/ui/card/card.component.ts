import {
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
} from '@angular/core';

export type CardPadding = 'sm' | 'md' | 'lg';

@Component({
  selector: 'app-card',
  standalone: true,
  templateUrl: './card.component.html',
  styleUrl: './card.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CardComponent {
  padding = input<CardPadding>('md');
  shadow = input<boolean>(false);

  protected readonly classes = computed(() => {
    const base = ['card', `card--padding-${this.padding()}`];
    if (this.shadow()) base.push('card--shadow');
    return base;
  });
}
