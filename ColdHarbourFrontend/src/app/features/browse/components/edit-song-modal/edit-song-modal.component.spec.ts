import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component, signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { EditSongModalComponent } from './edit-song-modal.component';

@Component({
  standalone: true,
  imports: [EditSongModalComponent],
  template: `<app-edit-song-modal
    [open]="open()"
    [title]="title"
    [trackNumber]="trackNumber"
    (save)="onSave($event)"
    (close)="closed = true"
  />`,
})
class HostComponent {
  open = signal(true);
  title = 'Comfortably Num';
  trackNumber: number | null = 5;
  saved: { title: string; trackNumber: number | null } | null = null;
  closed = false;
  onSave(e: { title: string; trackNumber: number | null }) {
    this.saved = e;
  }
}

describe('EditSongModalComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HostComponent] });
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  function setInput(selector: string, value: string) {
    const input = fixture.debugElement.query(By.css(selector))
      .nativeElement as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('seeds the fields from the inputs when opened', async () => {
    // NgModel defers its initial writeValue to a microtask.
    await fixture.whenStable();
    fixture.detectChanges();
    const title = fixture.debugElement.query(By.css('[data-test=title] input'))
      .nativeElement as HTMLInputElement;
    expect(title.value).toBe('Comfortably Num');
  });

  it('emits save with the edited values', () => {
    setInput('[data-test=title] input', 'Comfortably Numb');
    setInput('[data-test=trackNumber] input', '6');
    const saveBtn = fixture.debugElement.query(
      By.css('[data-test=save] button'),
    ).nativeElement as HTMLButtonElement;
    saveBtn.click();
    expect(host.saved).toEqual({ title: 'Comfortably Numb', trackNumber: 6 });
  });

  it('disables save when the title is blank', () => {
    setInput('[data-test=title] input', '   ');
    const saveBtn = fixture.debugElement.query(
      By.css('[data-test=save] button'),
    ).nativeElement as HTMLButtonElement;
    expect(saveBtn.disabled).toBeTrue();
  });

  it('emits close when cancel is clicked', () => {
    const cancel = fixture.debugElement.query(
      By.css('[data-test=cancel] button'),
    ).nativeElement as HTMLButtonElement;
    cancel.click();
    expect(host.closed).toBeTrue();
  });
});
