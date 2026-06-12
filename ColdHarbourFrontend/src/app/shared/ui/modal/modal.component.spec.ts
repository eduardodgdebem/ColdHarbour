import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component, signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { ModalComponent } from './modal.component';

@Component({
  standalone: true,
  imports: [ModalComponent],
  template: `
    <app-modal [isOpen]="isOpen()" [title]="title()" (close)="onClose()">
      <p>Body</p>
      <div slot="footer">
        <button>OK</button>
      </div>
    </app-modal>
  `,
})
class HostComponent {
  isOpen = signal(false);
  title = signal('Confirm');
  closed = 0;
  onClose() {
    this.closed++;
  }
}

describe('ModalComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HostComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('does not render the overlay when isOpen=false', () => {
    expect(fixture.debugElement.query(By.css('.modal'))).toBeNull();
  });

  it('renders the overlay and dialog when isOpen=true', () => {
    host.isOpen.set(true);
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('.modal'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('.modal__dialog'))).toBeTruthy();
  });

  it('renders the title when provided', () => {
    host.isOpen.set(true);
    fixture.detectChanges();
    const title = fixture.debugElement.query(By.css('.modal__title'));
    expect(title.nativeElement.textContent.trim()).toBe('Confirm');
  });

  it('projects the body and footer slots', () => {
    host.isOpen.set(true);
    fixture.detectChanges();
    const body = fixture.debugElement.query(By.css('.modal__body'));
    const footer = fixture.debugElement.query(By.css('.modal__footer'));
    expect(body.nativeElement.textContent).toContain('Body');
    expect(footer.nativeElement.querySelector('button')?.textContent).toBe(
      'OK',
    );
  });

  it('emits (close) when the backdrop is clicked', () => {
    host.isOpen.set(true);
    fixture.detectChanges();
    const backdrop = fixture.debugElement.query(By.css('.modal__backdrop'));
    backdrop.nativeElement.click();
    expect(host.closed).toBe(1);
  });

  it('does not emit (close) when the dialog itself is clicked', () => {
    host.isOpen.set(true);
    fixture.detectChanges();
    const dialog = fixture.debugElement.query(By.css('.modal__dialog'));
    dialog.nativeElement.click();
    expect(host.closed).toBe(0);
  });

  it('emits (close) when ESC is pressed while open', () => {
    host.isOpen.set(true);
    fixture.detectChanges();
    const event = new KeyboardEvent('keydown', { key: 'Escape' });
    document.dispatchEvent(event);
    expect(host.closed).toBe(1);
  });

  it('does not emit (close) on ESC when closed', () => {
    host.isOpen.set(false);
    fixture.detectChanges();
    const event = new KeyboardEvent('keydown', { key: 'Escape' });
    document.dispatchEvent(event);
    expect(host.closed).toBe(0);
  });
});
