import { Component, inject } from '@angular/core';
import { DatePipe, Location } from '@angular/common';
import { DeviceService } from '../../device.service';
import { PlaybackSessionService } from '../../../player/services/playback-session.service';

@Component({
  selector: 'app-devices-page',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './devices-page.component.html',
  styleUrl: './devices-page.component.scss',
})
export class DevicesPageComponent {
  protected readonly session = inject(PlaybackSessionService);
  protected readonly deviceService = inject(DeviceService);
  protected readonly myDeviceId = this.deviceService.getOrCreateDeviceId();
  private readonly location = inject(Location);

  transfer(deviceId: string): void {
    this.session.transferPlayback(deviceId);
  }

  back(): void {
    this.location.back();
  }
}
