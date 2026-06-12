import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { DatePipe } from '@angular/common';
import { DevicesPanelComponent } from './devices-panel.component';
import type { DeviceDto } from '../../services/playback-session.service';

function makeDevice(overrides: Partial<DeviceDto> = {}): DeviceDto {
  return {
    id: 'device-1',
    name: 'My Browser',
    lastSeenAt: '2024-01-01T12:00:00Z',
    ...overrides,
  };
}

describe('DevicesPanelComponent', () => {
  let fixture: ComponentFixture<DevicesPanelComponent>;
  let component: DevicesPanelComponent;

  function setUp(
    opts: {
      devices?: DeviceDto[];
      myDeviceId?: string;
      activeDeviceId?: string | null;
      trackId?: string | null;
      mobilePage?: boolean;
    } = {},
  ): void {
    TestBed.configureTestingModule({
      imports: [DevicesPanelComponent, DatePipe],
    });
    fixture = TestBed.createComponent(DevicesPanelComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('devices', opts.devices ?? []);
    fixture.componentRef.setInput('myDeviceId', opts.myDeviceId ?? 'device-1');
    fixture.componentRef.setInput(
      'activeDeviceId',
      opts.activeDeviceId ?? null,
    );
    fixture.componentRef.setInput('trackId', opts.trackId ?? null);
    if (opts.mobilePage) fixture.componentRef.setInput('mobilePage', true);
    fixture.detectChanges();
  }

  describe('empty state', () => {
    it('shows the empty message when no devices', () => {
      setUp({ devices: [] });
      expect(
        fixture.debugElement.query(By.css('.devices-panel__empty')),
      ).toBeTruthy();
    });

    it('hides the device list when no devices', () => {
      setUp({ devices: [] });
      expect(
        fixture.debugElement.query(By.css('.devices-panel__list')),
      ).toBeNull();
    });
  });

  describe('with devices', () => {
    const TWO_DEVICES: DeviceDto[] = [
      makeDevice({ id: 'device-1', name: 'Laptop' }),
      makeDevice({
        id: 'device-2',
        name: 'Phone',
        lastSeenAt: '2024-01-02T10:00:00Z',
      }),
    ];

    it('renders one row per device', () => {
      setUp({ devices: TWO_DEVICES });
      expect(
        fixture.debugElement.queryAll(By.css('.devices-panel__device')).length,
      ).toBe(2);
    });

    it('hides the empty state when devices are present', () => {
      setUp({ devices: TWO_DEVICES });
      expect(
        fixture.debugElement.query(By.css('.devices-panel__empty')),
      ).toBeNull();
    });

    it('applies --active class to the currently active device', () => {
      setUp({ devices: TWO_DEVICES, activeDeviceId: 'device-1' });
      const rows = fixture.debugElement.queryAll(
        By.css('.devices-panel__device'),
      );
      expect(rows[0].nativeElement.classList).toContain(
        'devices-panel__device--active',
      );
      expect(rows[1].nativeElement.classList).not.toContain(
        'devices-panel__device--active',
      );
    });

    it('applies --mine class to this device', () => {
      setUp({ devices: TWO_DEVICES, myDeviceId: 'device-2' });
      const rows = fixture.debugElement.queryAll(
        By.css('.devices-panel__device'),
      );
      expect(rows[0].nativeElement.classList).not.toContain(
        'devices-panel__device--mine',
      );
      expect(rows[1].nativeElement.classList).toContain(
        'devices-panel__device--mine',
      );
    });

    it('does not show PLAY HERE when no track is loaded', () => {
      setUp({
        devices: TWO_DEVICES,
        trackId: null,
        activeDeviceId: 'device-1',
      });
      expect(
        fixture.debugElement.query(By.css('.devices-panel__play-here')),
      ).toBeNull();
    });

    it('does not show PLAY HERE on the active device even when a track is loaded', () => {
      setUp({
        devices: TWO_DEVICES,
        trackId: 'track-1',
        activeDeviceId: 'device-1',
      });
      const rows = fixture.debugElement.queryAll(
        By.css('.devices-panel__device'),
      );
      const activeRow = rows[0];
      expect(activeRow.query(By.css('.devices-panel__play-here'))).toBeNull();
    });

    it('shows PLAY HERE on an inactive device when a track is loaded', () => {
      setUp({
        devices: TWO_DEVICES,
        trackId: 'track-1',
        activeDeviceId: 'device-1',
      });
      const rows = fixture.debugElement.queryAll(
        By.css('.devices-panel__device'),
      );
      const inactiveRow = rows[1];
      expect(
        inactiveRow.query(By.css('.devices-panel__play-here')),
      ).toBeTruthy();
    });
  });

  describe('outputs', () => {
    const TWO_DEVICES: DeviceDto[] = [
      makeDevice({ id: 'device-1', name: 'Laptop' }),
      makeDevice({
        id: 'device-2',
        name: 'Phone',
        lastSeenAt: '2024-01-02T10:00:00Z',
      }),
    ];

    it('emits close when the close button is clicked', () => {
      setUp({ devices: TWO_DEVICES });
      spyOn(component.close, 'emit');
      fixture.debugElement
        .query(By.css('.devices-panel__close'))
        .nativeElement.click();
      expect(component.close.emit).toHaveBeenCalled();
    });

    it('emits transfer with the device id when PLAY HERE is clicked', () => {
      setUp({
        devices: TWO_DEVICES,
        trackId: 'track-1',
        activeDeviceId: 'device-1',
      });
      spyOn(component.transfer, 'emit');
      fixture.debugElement
        .query(By.css('.devices-panel__play-here'))
        .nativeElement.click();
      expect(component.transfer.emit).toHaveBeenCalledWith('device-2');
    });
  });

  describe('mobilePage input', () => {
    it('host does not have --mobile-page class by default', () => {
      setUp({});
      expect((fixture.nativeElement as HTMLElement).classList).not.toContain(
        'devices-panel--mobile-page',
      );
    });

    it('host gains --mobile-page class when mobilePage is true', () => {
      setUp({ mobilePage: true });
      expect((fixture.nativeElement as HTMLElement).classList).toContain(
        'devices-panel--mobile-page',
      );
    });
  });
});
