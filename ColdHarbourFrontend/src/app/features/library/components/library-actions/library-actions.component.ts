import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { LibraryService } from '../../library.service';
import type { LibrarySyncDiff } from '../../../../core/api/api.service';
import {
  ButtonComponent,
  ModalComponent,
} from '../../../../shared/ui';

@Component({
  selector: 'app-library-actions',
  standalone: true,
  imports: [ButtonComponent, ModalComponent],
  templateUrl: './library-actions.component.html',
  styleUrl: './library-actions.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LibraryActionsComponent {
  readonly libraryService = inject(LibraryService);

  onFilesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!input.files?.length) return;
    Array.from(input.files).forEach((file) =>
      this.libraryService.uploadFile(file),
    );
    input.value = '';
  }

  previewSync(): void {
    this.libraryService.previewSync();
  }

  applySync(): void {
    this.libraryService.applySync();
  }

  closeSync(): void {
    this.libraryService.syncDiff.set(null);
  }

  diff(): LibrarySyncDiff | null {
    return this.libraryService.syncDiff();
  }
}
