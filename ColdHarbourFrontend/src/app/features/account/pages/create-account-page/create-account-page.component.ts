import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService, RegisterPayload } from '../../../../core/auth/auth.service';
import {
  BackButtonComponent,
  ButtonComponent,
  FormFieldComponent,
  InputComponent,
} from '../../../../shared/ui';

@Component({
  selector: 'app-create-account-page',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    BackButtonComponent,
    ButtonComponent,
    FormFieldComponent,
    InputComponent,
  ],
  templateUrl: './create-account-page.component.html',
  styleUrl: './create-account-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CreateAccountPageComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly form = new FormGroup({
    name: new FormControl('', { nonNullable: true }),
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email],
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.minLength(8)],
    }),
    confirmPassword: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
  });

  readonly error = signal<string | null>(null);
  readonly submitting = signal(false);

  submit(): void {
    this.error.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { name, email, password, confirmPassword } = this.form.getRawValue();

    if (password !== confirmPassword) {
      this.error.set('Password and confirmation must match.');
      return;
    }

    const payload: RegisterPayload = { email, password };
    if (name.trim()) payload.name = name.trim();

    this.submitting.set(true);
    this.auth.register(payload).subscribe({
      next: () => {
        this.submitting.set(false);
        this.router.navigate(['/account']);
      },
      error: () => {
        this.error.set(
          'Could not create the user. The email may already be taken, or you may lack the Owner role.',
        );
        this.submitting.set(false);
      },
    });
  }

  emailError(): string | null {
    const c = this.form.controls.email;
    return c.invalid && c.touched ? 'Enter a valid email address.' : null;
  }

  passwordError(): string | null {
    const c = this.form.controls.password;
    return c.invalid && c.touched ? 'Minimum 8 characters.' : null;
  }

  confirmPasswordError(): string | null {
    const c = this.form.controls.confirmPassword;
    return c.invalid && c.touched ? 'Required.' : null;
  }
}
