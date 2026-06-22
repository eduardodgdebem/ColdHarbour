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

export interface EditSongPayload {
  title: string;
  trackNumber: number | null;
}

@Component({
  selector: 'app-edit-song-modal',
  standalone: true,
  imports: [
    FormsModule,
    ModalComponent,
    FormFieldComponent,
    InputComponent,
    ButtonComponent,
  ],
  templateUrl: './edit-song-modal.component.html',
  styleUrl: './edit-song-modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EditSongModalComponent {
  readonly open = input(false);
  readonly title = input('');
  readonly trackNumber = input<number | null>(null);
  readonly saving = input(false);
  readonly errorMessage = input<string | null>(null);

  readonly save = output<EditSongPayload>();
  readonly close = output<void>();

  // Seeded from the inputs and re-seeded whenever the modal (re)opens; writable so
  // the user's edits stick until the next open. linkedSignal (vs. an effect) makes
  // the seed part of the first render, so the bound input reflects it immediately.
  protected readonly titleValue = linkedSignal(() => {
    this.open();
    return this.title();
  });
  protected readonly trackNumberValue = linkedSignal(() => {
    this.open();
    return this.trackNumber()?.toString() ?? '';
  });

  protected readonly canSave = computed(
    () => this.titleValue().trim().length > 0,
  );

  protected submit(): void {
    if (!this.canSave() || this.saving()) return;
    const raw = this.trackNumberValue().trim();
    const trackNumber = raw === '' ? null : Number(raw);
    this.save.emit({ title: this.titleValue().trim(), trackNumber });
  }
}
