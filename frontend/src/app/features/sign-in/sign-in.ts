import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

import { AuthService } from '../../core/auth/auth.service';

/**
 * Local-development sign-in: exchanges an allow-listed username for a bearer
 * token from the API. In Entra ID mode this screen is replaced by a redirect
 * to the identity provider, and the API stops serving the token endpoint.
 */
@Component({
  selector: 'app-sign-in',
  imports: [ReactiveFormsModule],
  templateUrl: './sign-in.html',
  styleUrl: './sign-in.scss',
})
export class SignIn {
  private readonly formBuilder = inject(NonNullableFormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly form = this.formBuilder.group({
    username: ['dev.reviewer', [Validators.required]],
  });

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected get usernameControl() {
    return this.form.controls.username;
  }

  protected showUsernameError(): boolean {
    return (
      this.usernameControl.invalid &&
      (this.usernameControl.touched || this.usernameControl.dirty)
    );
  }

  protected onSubmit(): void {
    this.errorMessage.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    const { username } = this.form.getRawValue();

    this.auth.signIn(username.trim()).subscribe({
      next: () => {
        this.submitting.set(false);
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/';
        void this.router.navigateByUrl(returnUrl);
      },
      error: (error: HttpErrorResponse) => {
        this.submitting.set(false);
        this.errorMessage.set(describeFailure(error));
      },
    });
  }
}

function describeFailure(error: HttpErrorResponse): string {
  if (error.status === 400) {
    return 'That username is not in the development user allow list.';
  }
  if (error.status === 404) {
    return 'Development sign-in is disabled. This environment uses Microsoft Entra ID.';
  }
  return 'Sign-in failed. Verify the API is running and try again.';
}
