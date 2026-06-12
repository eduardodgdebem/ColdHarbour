import {
  ChangeDetectionStrategy,
  Component,
  input,
  output,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import type { DeviceDto } from '../../services/playback-session.service';

@Component({
  selector: 'app-devices-panel',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './devices-panel.component.html',
  styleUrl: './devices-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'devices-panel',
    '[class.devices-panel--mobile-page]': 'mobilePage()',
  },
})
export class DevicesPanelComponent {
  readonly devices = input.required<DeviceDto[]>();
  readonly myDeviceId = input.required<string>();
  readonly activeDeviceId = input<string | null>(null);
  readonly trackId = input<string | null>(null);
  readonly mobilePage = input(false);

  readonly close = output<void>();
  readonly transfer = output<string>();
}
