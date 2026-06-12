import {
  ChangeDetectionStrategy,
  Component,
  inject,
  signal,
} from '@angular/core';
import {
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../../core/auth/auth.service';
import {
  BackButtonComponent,
  BadgeComponent,
  ButtonComponent,
  FormFieldComponent,
  InputComponent,
} from '../../../../shared/ui';

@Component({
  selector: 'app-account-page',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    BackButtonComponent,
    BadgeComponent,
    ButtonComponent,
    FormFieldComponent,
    InputComponent,
  ],
  templateUrl: './account-page.component.html',
  styleUrl: './account-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountPageComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly email = this.auth.email;
  readonly name = this.auth.name;
  readonly role = this.auth.role;

  readonly passwordForm = new FormGroup({
    currentPassword: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
    newPassword: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.minLength(8)],
    }),
    confirmPassword: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
  });

  readonly passwordError = signal<string | null>(null);
  readonly passwordSuccess = signal(false);
  readonly passwordSubmitting = signal(false);

  isOwner(): boolean {
    return this.auth.isOwner();
  }

  signOut(): void {
    this.auth.logout().subscribe({
      next: () => this.router.navigate(['/login']),
      error: () => this.router.navigate(['/login']),
    });
  }

  submitPasswordChange(): void {
    this.passwordError.set(null);
    this.passwordSuccess.set(false);

    if (this.passwordForm.invalid) {
      this.passwordForm.markAllAsTouched();
      return;
    }

    const { currentPassword, newPassword, confirmPassword } =
      this.passwordForm.getRawValue();

    if (newPassword !== confirmPassword) {
      this.passwordError.set('New password and confirmation must match.');
      return;
    }

    this.passwordSubmitting.set(true);
    this.auth.changePassword(currentPassword, newPassword).subscribe({
      next: () => {
        this.passwordSuccess.set(true);
        this.passwordSubmitting.set(false);
        this.passwordForm.reset();
      },
      error: () => {
        this.passwordError.set(
          'Could not change password. Try again, or check that your current password is correct.',
        );
        this.passwordSubmitting.set(false);
      },
    });
  }

  newPasswordError(): string | null {
    const c = this.passwordForm.controls.newPassword;
    return c.invalid && c.touched ? 'Minimum 8 characters.' : null;
  }

  currentPasswordError(): string | null {
    const c = this.passwordForm.controls.currentPassword;
    return c.invalid && c.touched ? 'Required.' : null;
  }

  confirmPasswordError(): string | null {
    const c = this.passwordForm.controls.confirmPassword;
    return c.invalid && c.touched ? 'Required.' : null;
  }
}
