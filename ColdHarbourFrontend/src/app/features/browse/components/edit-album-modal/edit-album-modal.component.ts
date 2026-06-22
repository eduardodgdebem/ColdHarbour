import {
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
  linkedSignal,
  output,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  ButtonComponent,
  FormFieldComponent,
  InputComponent,
  ModalComponent,
} from '../../../../shared/ui';

export interface EditAlbumPayload {
  title: string;
  year: number | null;
  coverFile: File | null;
}

@Component({
  selector: 'app-edit-album-modal',
  standalone: true,
  imports: [
    FormsModule,
    ModalComponent,
    FormFieldComponent,
    InputComponent,
    ButtonComponent,
  ],
  templateUrl: './edit-album-modal.component.html',
  styleUrl: './edit-album-modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EditAlbumModalComponent {
  readonly open = input(false);
  readonly title = input('');
  readonly year = input<number | null>(null);
  readonly coverRef = input('');
  readonly saving = input(false);
  readonly errorMessage = input<string | null>(null);

  readonly save = output<EditAlbumPayload>();
  readonly close = output<void>();

  // linkedSignals seed from the inputs and re-seed on each (re)open; writable so the
  // user's edits and picked cover persist within an open session.
  protected readonly titleValue = linkedSignal(() => {
    this.open();
    return this.title();
  });
  protected readonly yearValue = linkedSignal(() => {
    this.open();
    return this.year()?.toString() ?? '';
  });
  protected readonly coverFile = linkedSignal<File | null>(() => {
    this.open();
    return null;
  });
  protected readonly coverPreview = linkedSignal<string | null>(() => {
    this.open();
    return null;
  });

  protected readonly canSave = computed(
    () => this.titleValue().trim().length > 0,
  );

  protected onFilePicked(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0] ?? null;
    this.coverFile.set(file);
    this.coverPreview.set(file ? URL.createObjectURL(file) : null);
  }

  protected submit(): void {
    if (!this.canSave() || this.saving()) return;
    const raw = this.yearValue().trim();
    const year = raw === '' ? null : Number(raw);
    this.save.emit({
      title: this.titleValue().trim(),
      year,
      coverFile: this.coverFile(),
    });
  }
}
