import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

export interface QueueItem {
  index: number;
  trackId: string;
  name: string;
  author: string;
  isCurrent: boolean;
}

@Component({
  selector: 'app-queue-panel',
  standalone: true,
  imports: [],
  templateUrl: './queue-panel.component.html',
  styleUrl: './queue-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'stage__queue',
    '[class.stage__queue--mobile-page]': 'mobilePage()',
  },
})
export class QueuePanelComponent {
  readonly items = input.required<QueueItem[]>();
  readonly mobilePage = input(false);

  readonly close = output<void>();
  readonly clear = output<void>();
  readonly moveUp = output<number>();
  readonly moveDown = output<number>();
  readonly remove = output<number>();
}
