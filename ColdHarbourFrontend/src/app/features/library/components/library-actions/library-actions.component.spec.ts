import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { LibraryActionsComponent } from './library-actions.component';
import { LibraryService } from '../../library.service';

describe('LibraryActionsComponent', () => {
  let fixture: ComponentFixture<LibraryActionsComponent>;
  let component: LibraryActionsComponent;
  let librarySpy: jasmine.SpyObj<LibraryService>;

  beforeEach(async () => {
    librarySpy = jasmine.createSpyObj(
      'LibraryService',
      ['uploadFile', 'previewSync', 'applySync'],
      {
        isUploading: signal(false),
        uploadError: signal<string | null>(null),
        isSyncing: signal(false),
        syncDiff: signal<unknown | null>(null),
        syncError: signal<string | null>(null),
      },
    );

    await TestBed.configureTestingModule({
      imports: [LibraryActionsComponent],
      providers: [{ provide: LibraryService, useValue: librarySpy }],
    }).compileComponents();

    fixture = TestBed.createComponent(LibraryActionsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('renders an ADD TRACKS label and a SYNC LIBRARY button', () => {
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('ADD TRACKS');
    expect(text).toContain('SYNC LIBRARY');
  });

  it('uploads each selected file via the library service', () => {
    const file1 = new File(['a'], 'a.mp3', { type: 'audio/mpeg' });
    const file2 = new File(['b'], 'b.flac', { type: 'audio/flac' });
    const input = fixture.debugElement.query(By.css('input[type="file"]'))
      .nativeElement as HTMLInputElement;
    Object.defineProperty(input, 'files', {
      value: [file1, file2],
      writable: false,
    });
    input.dispatchEvent(new Event('change'));

    expect(librarySpy.uploadFile).toHaveBeenCalledTimes(2);
    expect(librarySpy.uploadFile).toHaveBeenCalledWith(file1);
    expect(librarySpy.uploadFile).toHaveBeenCalledWith(file2);
  });

  it('shows the upload error strip when the service has an upload error', () => {
    expect(fixture.debugElement.query(By.css('.upload-error'))).toBeNull();
    (librarySpy.uploadError as ReturnType<typeof signal<string | null>>).set(
      'boom',
    );
    fixture.detectChanges();
    const err = fixture.debugElement.query(By.css('.upload-error'));
    expect(err.nativeElement.textContent).toContain('boom');
  });

  it('triggers previewSync when SYNC LIBRARY is clicked', () => {
    component.previewSync();
    expect(librarySpy.previewSync).toHaveBeenCalled();
  });

  it('opens the modal when syncDiff is set', () => {
    expect(fixture.debugElement.query(By.css('.modal'))).toBeNull();
    (librarySpy.syncDiff as ReturnType<typeof signal<unknown | null>>).set({
      added: [],
      missing: [],
      renamed: [],
    });
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('.modal'))).toBeTruthy();
  });

  it('applies the sync when applySync() is called', () => {
    component.applySync();
    expect(librarySpy.applySync).toHaveBeenCalled();
  });

  it('clears the syncDiff signal when closeSync() is called', () => {
    const syncDiff = librarySpy.syncDiff as ReturnType<
      typeof signal<unknown | null>
    >;
    syncDiff.set({ added: [], missing: [], renamed: [] });
    component.closeSync();
    expect(syncDiff()).toBeNull();
  });
});
