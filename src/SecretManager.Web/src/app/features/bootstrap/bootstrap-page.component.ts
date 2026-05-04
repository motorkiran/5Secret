import { CommonModule } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { SessionStore } from '../../core/state/session.store';

@Component({
  selector: 'app-bootstrap-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './bootstrap-page.component.html',
  styleUrl: './bootstrap-page.component.scss'
})
export class BootstrapPageComponent {
  private readonly formBuilder = inject(FormBuilder);
  private readonly router = inject(Router);
  protected readonly sessionStore = inject(SessionStore);

  protected readonly form = this.formBuilder.nonNullable.group({
    installationName: ['Secret Manager', [Validators.required, Validators.minLength(3)]],
    ownerDisplayName: ['', [Validators.required, Validators.minLength(3)]],
    ownerUsername: ['', [Validators.required, Validators.minLength(3)]],
    password: ['', [Validators.required, Validators.minLength(12)]],
    confirmPassword: ['', [Validators.required]]
  });

  protected readonly passwordsMismatch = computed(() => {
    const { password, confirmPassword } = this.form.getRawValue();
    return confirmPassword.length > 0 && password !== confirmPassword;
  });

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.passwordsMismatch()) {
      this.form.markAllAsTouched();
      return;
    }

    const { confirmPassword: _, ...request } = this.form.getRawValue();
    const completed = await this.sessionStore.bootstrap(request);
    if (completed) {
      await this.router.navigateByUrl('/app');
    }
  }
}