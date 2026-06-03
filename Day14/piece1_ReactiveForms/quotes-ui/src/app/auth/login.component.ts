import { Component, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators, AbstractControl } from '@angular/forms';
import { AuthService } from './auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './login.component.html',
})
export class LoginComponent {
  private fb  = inject(FormBuilder);
  private svc = inject(AuthService);

  submitting = signal(false);
  loginError = signal<string | null>(null);

  form = this.fb.group({
    email:    ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(6)]],
  });

  get email(): AbstractControl    { return this.form.controls.email; }
  get password(): AbstractControl { return this.form.controls.password; }

  isInvalid(ctrl: AbstractControl): boolean {
    return ctrl.invalid && (ctrl.dirty || ctrl.touched);
  }

  onSubmit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.submitting.set(true);
    this.loginError.set(null);

    this.svc.login({
      email:    this.form.value.email!,
      password: this.form.value.password!,
    }).subscribe({
      next: () => this.submitting.set(false),
      error: (e: Error) => {
        this.submitting.set(false);
        this.loginError.set(e.message);
      },
    });
  }
}
