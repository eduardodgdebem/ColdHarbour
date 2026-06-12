import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { QueuePanelComponent, QueueItem } from './queue-panel.component';

function makeItem(overrides: Partial<QueueItem> = {}): QueueItem {
  return {
    index: 0,
    trackId: 'track-1',
    name: 'Test Track',
    author: 'Test Artist',
    isCurrent: false,
    ...overrides,
  };
}

describe('QueuePanelComponent', () => {
  let fixture: ComponentFixture<QueuePanelComponent>;
  let component: QueuePanelComponent;

  function setUp(items: QueueItem[] = [], mobilePage = false): void {
    TestBed.configureTestingModule({ imports: [QueuePanelComponent] });
    fixture = TestBed.createComponent(QueuePanelComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('items', items);
    if (mobilePage) fixture.componentRef.setInput('mobilePage', true);
    fixture.detectChanges();
  }

  describe('empty state', () => {
    it('shows the empty manifest section when no items', () => {
      setUp([]);
      expect(
        fixture.debugElement.query(By.css('.stage__queue-empty')),
      ).toBeTruthy();
    });

    it('hides the list when no items', () => {
      setUp([]);
      expect(
        fixture.debugElement.query(By.css('.stage__queue-list')),
      ).toBeNull();
    });

    it('disables the clear button when no items', () => {
      setUp([]);
      const btn = fixture.debugElement.query(By.css('.stage__queue-clear'))
        .nativeElement as HTMLButtonElement;
      expect(btn.disabled).toBeTrue();
    });

    it('does not render the track count when no items', () => {
      setUp([]);
      expect(
        fixture.debugElement.query(By.css('.stage__queue-track-count')),
      ).toBeNull();
    });
  });

  describe('with items', () => {
    const THREE_ITEMS: QueueItem[] = [
      makeItem({
        index: 0,
        name: 'Alpha',
        author: 'Artist A',
        isCurrent: true,
      }),
      makeItem({
        index: 1,
        trackId: 'track-2',
        name: 'Beta',
        author: 'Artist B',
      }),
      makeItem({
        index: 2,
        trackId: 'track-3',
        name: 'Gamma',
        author: 'Artist C',
      }),
    ];

    it('renders one row per item', () => {
      setUp(THREE_ITEMS);
      expect(
        fixture.debugElement.queryAll(By.css('.stage__queue-row')).length,
      ).toBe(3);
    });

    it('shows the track count badge', () => {
      setUp(THREE_ITEMS);
      const badge = fixture.debugElement.query(
        By.css('.stage__queue-track-count'),
      );
      expect(badge).toBeTruthy();
      expect((badge.nativeElement as HTMLElement).textContent).toContain(
        '3 TRACKS',
      );
    });

    it('hides the empty state when items are present', () => {
      setUp(THREE_ITEMS);
      expect(
        fixture.debugElement.query(By.css('.stage__queue-empty')),
      ).toBeNull();
    });

    it('enables the clear button when items are present', () => {
      setUp(THREE_ITEMS);
      const btn = fixture.debugElement.query(By.css('.stage__queue-clear'))
        .nativeElement as HTMLButtonElement;
      expect(btn.disabled).toBeFalse();
    });

    it('applies --current modifier class only to the current item', () => {
      setUp(THREE_ITEMS);
      const rows = fixture.debugElement.queryAll(By.css('.stage__queue-row'));
      expect(rows[0].nativeElement.classList).toContain(
        'stage__queue-row--current',
      );
      expect(rows[1].nativeElement.classList).not.toContain(
        'stage__queue-row--current',
      );
      expect(rows[2].nativeElement.classList).not.toContain(
        'stage__queue-row--current',
      );
    });

    it('disables move-up on the first item', () => {
      setUp(THREE_ITEMS);
      const btns = fixture.debugElement
        .queryAll(By.css('.stage__queue-row'))[0]
        .queryAll(By.css('.stage__queue-btn'));
      expect((btns[0].nativeElement as HTMLButtonElement).disabled).toBeTrue();
    });

    it('disables move-down on the last item', () => {
      setUp(THREE_ITEMS);
      const btns = fixture.debugElement
        .queryAll(By.css('.stage__queue-row'))[2]
        .queryAll(By.css('.stage__queue-btn'));
      expect((btns[1].nativeElement as HTMLButtonElement).disabled).toBeTrue();
    });

    it('does not disable move-up for a middle item', () => {
      setUp(THREE_ITEMS);
      const btns = fixture.debugElement
        .queryAll(By.css('.stage__queue-row'))[1]
        .queryAll(By.css('.stage__queue-btn'));
      expect((btns[0].nativeElement as HTMLButtonElement).disabled).toBeFalse();
    });

    it('does not disable move-down for a middle item', () => {
      setUp(THREE_ITEMS);
      const btns = fixture.debugElement
        .queryAll(By.css('.stage__queue-row'))[1]
        .queryAll(By.css('.stage__queue-btn'));
      expect((btns[1].nativeElement as HTMLButtonElement).disabled).toBeFalse();
    });
  });

  describe('outputs', () => {
    const TWO_ITEMS: QueueItem[] = [
      makeItem({ index: 0, name: 'Alpha', isCurrent: true }),
      makeItem({ index: 1, trackId: 'track-2', name: 'Beta' }),
    ];

    it('emits close when the close button is clicked', () => {
      setUp(TWO_ITEMS);
      spyOn(component.close, 'emit');
      fixture.debugElement
        .query(By.css('.stage__queue-close'))
        .nativeElement.click();
      expect(component.close.emit).toHaveBeenCalled();
    });

    it('emits clear when the clear button is clicked', () => {
      setUp(TWO_ITEMS);
      spyOn(component.clear, 'emit');
      fixture.debugElement
        .query(By.css('.stage__queue-clear'))
        .nativeElement.click();
      expect(component.clear.emit).toHaveBeenCalled();
    });

    it('emits moveUp with the item index when ▲ is clicked', () => {
      setUp(TWO_ITEMS);
      spyOn(component.moveUp, 'emit');
      // Second row (index 1) — move-up is enabled
      const btns = fixture.debugElement
        .queryAll(By.css('.stage__queue-row'))[1]
        .queryAll(By.css('.stage__queue-btn'));
      btns[0].nativeElement.click();
      expect(component.moveUp.emit).toHaveBeenCalledWith(1);
    });

    it('emits moveDown with the item index when ▼ is clicked', () => {
      setUp(TWO_ITEMS);
      spyOn(component.moveDown, 'emit');
      // First row (index 0) — move-down is enabled
      const btns = fixture.debugElement
        .queryAll(By.css('.stage__queue-row'))[0]
        .queryAll(By.css('.stage__queue-btn'));
      btns[1].nativeElement.click();
      expect(component.moveDown.emit).toHaveBeenCalledWith(0);
    });

    it('emits remove with the item index when ✕ is clicked', () => {
      setUp(TWO_ITEMS);
      spyOn(component.remove, 'emit');
      const btns = fixture.debugElement
        .queryAll(By.css('.stage__queue-row'))[0]
        .queryAll(By.css('.stage__queue-btn'));
      btns[2].nativeElement.click();
      expect(component.remove.emit).toHaveBeenCalledWith(0);
    });
  });

  describe('mobilePage input', () => {
    it('host does not have stage__queue--mobile-page class by default', () => {
      setUp([]);
      expect((fixture.nativeElement as HTMLElement).classList).not.toContain(
        'stage__queue--mobile-page',
      );
    });

    it('host gains stage__queue--mobile-page class when mobilePage is true', () => {
      setUp([], true);
      expect((fixture.nativeElement as HTMLElement).classList).toContain(
        'stage__queue--mobile-page',
      );
    });
  });
});
