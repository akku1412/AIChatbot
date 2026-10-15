import { Component, ElementRef, afterNextRender, inject, input, signal, viewChild } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';

import { AuthService } from '../../core/auth/auth.service';
import { GoogleIdentityService } from '../../core/auth/google-identity.service';
import { errorMessage, safeReturnUrl } from '../../core/http/api-error';
import { AppLogo } from '../../shared/components/app-logo/app-logo';
import { AuthShell } from '../../shared/components/auth-shell/auth-shell';
import { LoadingSpinner } from '../../shared/components/loading-spinner/loading-spinner';
import { NotificationService } from '../../shared/components/toast/notification.service';

/** Same rule as the API: something@domain.tld */
const EMAIL_PATTERN = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, AppLogo, AuthShell, LoadingSpinner],
  templateUrl: './login.html',
  styleUrl: './login.css',
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly googleIdentity = inject(GoogleIdentityService);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);

  /** Query params (bound by withComponentInputBinding). */
  readonly returnUrl = input<string>();
  readonly expired = input<string>();
  /** Prefilled when the user chose "change email" on the code page. */
  readonly email = input<string>();

  private readonly buttonHost = viewChild.required<ElementRef<HTMLDivElement>>('googleButton');

  protected readonly emailForm = new FormGroup({
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(256), Validators.pattern(EMAIL_PATTERN)],
    }),
  });
  protected readonly emailControl = this.emailForm.controls.email;

  protected readonly buttonReady = signal(false);
  protected readonly googleSigningIn = signal(false);
  protected readonly sendingCode = signal(false);
  protected readonly googleError = signal<string | null>(null);
  protected readonly emailError = signal<string | null>(null);

  constructor() {
    // The Google button needs a real DOM element, so render after the view exists.
    afterNextRender(() => {
      const prefill = this.email();
      if (prefill) {
        this.emailControl.setValue(prefill);
      }
      this.renderGoogleButton();
    });
  }

  protected async renderGoogleButton(): Promise<void> {
    this.googleError.set(null);
    const host = this.buttonHost().nativeElement;
    const dark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    try {
      // Measure only after web fonts and layout have settled, or the width comes out too small.
      await document.fonts.ready;
      await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));
      const available = Math.floor(host.getBoundingClientRect().width) || 400;

      await this.googleIdentity.renderButton(host, (idToken) => this.onGoogleCredential(idToken), {
        theme: dark ? 'filled_black' : 'outline',
        // Google accepts 200-400px; match the form width.
        width: Math.max(200, Math.min(400, available)),
      });
      this.buttonReady.set(true);
    } catch (e) {
      this.googleError.set(e instanceof Error ? e.message : 'Could not load Google sign-in.');
    }
  }

  /** Email sign-in, step 1. */
  protected sendCode(): void {
    if (this.emailForm.invalid || this.sendingCode()) {
      this.emailControl.markAsTouched();
      return;
    }

    this.sendingCode.set(true);
    this.emailError.set(null);
    this.auth.sendOtp(this.emailControl.value).subscribe({
      next: () => {
        const returnUrl = this.returnUrl();
        this.router.navigate(['/verify-otp'], { queryParams: returnUrl ? { returnUrl } : {} });
      },
      error: (err: unknown) => {
        this.sendingCode.set(false);
        this.emailError.set(errorMessage(err));
      },
    });
  }

  /** Google sign-in: verified by the API, no code needed. */
  private onGoogleCredential(idToken: string): void {
    this.googleSigningIn.set(true);
    this.googleError.set(null);

    this.auth.signInWithGoogle(idToken).subscribe({
      next: (user) => {
        this.notifications.success(`Welcome, ${user.fullName.split(' ')[0]}!`);
        this.router.navigateByUrl(safeReturnUrl(this.returnUrl()));
      },
      error: (err: unknown) => {
        this.googleSigningIn.set(false);
        this.googleError.set(errorMessage(err));
      },
    });
  }
}
