import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component, signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import {
  EditAlbumModalComponent,
  type EditAlbumPayload,
} from './edit-album-modal.component';

@Component({
  standalone: true,
  imports: [EditAlbumModalComponent],
  template: `<app-edit-album-modal
    [open]="open()"
    [title]="title"
    [year]="year"
    [coverRef]="coverRef"
    (save)="onSave($event)"
    (close)="closed = true"
  />`,
})
class HostComponent {
  open = signal(true);
  title = 'The Wal';
  year: number | null = 1978;
  coverRef = '/api/artwork/album-1?size=256';
  saved: EditAlbumPayload | null = null;
  closed = false;
  onSave(e: EditAlbumPayload) {
    this.saved = e;
  }
}

describe('EditAlbumModalComponent', () => {
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

  it('seeds title and year from inputs', async () => {
    // NgModel defers its initial writeValue to a microtask.
    await fixture.whenStable();
    fixture.detectChanges();
    const title = fixture.debugElement.query(By.css('[data-test=title] input'))
      .nativeElement as HTMLInputElement;
    expect(title.value).toBe('The Wal');
  });

  it('emits save with edited title, year and a null cover when no file picked', () => {
    setInput('[data-test=title] input', 'The Wall');
    setInput('[data-test=year] input', '1979');
    const saveBtn = fixture.debugElement.query(
      By.css('[data-test=save] button'),
    ).nativeElement as HTMLButtonElement;
    saveBtn.click();
    expect(host.saved).toEqual({
      title: 'The Wall',
      year: 1979,
      coverFile: null,
    });
  });

  it('includes the picked cover file in the save payload', () => {
    const file = new File(['x'], 'cover.jpg', { type: 'image/jpeg' });
    const fileInput = fixture.debugElement.query(
      By.css('[data-test=cover] input'),
    ).nativeElement as HTMLInputElement;
    Object.defineProperty(fileInput, 'files', { value: [file] });
    fileInput.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const saveBtn = fixture.debugElement.query(
      By.css('[data-test=save] button'),
    ).nativeElement as HTMLButtonElement;
    saveBtn.click();
    expect(host.saved?.coverFile).toBe(file);
  });

  it('disables save when the title is blank', () => {
    setInput('[data-test=title] input', '');
    const saveBtn = fixture.debugElement.query(
      By.css('[data-test=save] button'),
    ).nativeElement as HTMLButtonElement;
    expect(saveBtn.disabled).toBeTrue();
  });
});
