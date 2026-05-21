import { Component, signal } from '@angular/core';
import {
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../../../core/auth/auth.service';
import { DeviceService } from '../../../../features/devices/device.service';
import {
  ButtonComponent,
  FormFieldComponent,
  InputComponent,
} from '../../../../shared/ui';

@Component({
  selector: 'app-login-page',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    ButtonComponent,
    InputComponent,
    FormFieldComponent,
  ],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.scss',
})
export class LoginPageComponent {
  form = new FormGroup({
    email: new FormControl('', [Validators.required, Validators.email]),
    password: new FormControl('', [
      Validators.required,
      Validators.minLength(8),
    ]),
  });

  error = signal<string | null>(null);
  loading = signal(false);

  constructor(
    private auth: AuthService,
    private device: DeviceService,
    private router: Router,
  ) {}

  emailError(): string | null {
    const c = this.form.controls.email;
    return c.invalid && c.touched ? 'Enter a valid email address.' : null;
  }

  passwordError(): string | null {
    const c = this.form.controls.password;
    return c.invalid && c.touched ? 'Minimum 8 characters.' : null;
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.loading.set(true);
    this.error.set(null);
    const { email, password } = this.form.value;
    this.auth.login(email!, password!).subscribe({
      next: () => {
        this.device.register().subscribe();
        this.router.navigate(['/']);
      },
      error: () => {
        this.error.set('Invalid email or password.');
        this.loading.set(false);
      },
    });
  }
}
