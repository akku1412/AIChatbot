import { Component, ElementRef, afterNextRender, inject, input, signal, viewChild } from '@angular/core';
import { Router } from '@angular/router';

import { AuthService } from '../../core/auth/auth.service';
import { GoogleIdentityService } from '../../core/auth/google-identity.service';
import { errorMessage } from '../../core/http/api-error';
import { AppLogo } from '../../shared/components/app-logo/app-logo';
import { AuthShell } from '../../shared/components/auth-shell/auth-shell';
import { LoadingSpinner } from '../../shared/components/loading-spinner/loading-spinner';

@Component({
  selector: 'app-login',
  imports: [AppLogo, AuthShell, LoadingSpinner],
  templateUrl: './login.html',
  styleUrl: './login.css',
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly googleIdentity = inject(GoogleIdentityService);
  private readonly router = inject(Router);

  /** Query params (bound by withComponentInputBinding). */
  readonly returnUrl = input<string>();
  readonly expired = input<string>();

  private readonly buttonHost = viewChild.required<ElementRef<HTMLDivElement>>('googleButton');

  protected readonly buttonReady = signal(false);
  protected readonly signingIn = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    // The Google button needs a real DOM element, so render after the view exists.
    afterNextRender(() => this.renderGoogleButton());
  }

  protected async renderGoogleButton(): Promise<void> {
    this.error.set(null);
    const host = this.buttonHost().nativeElement;
    const dark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    try {
      // Measure only after web fonts and layout have settled, or the width comes out too small.
      await document.fonts.ready;
      await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));
      const available = Math.floor(host.getBoundingClientRect().width) || 400;

      await this.googleIdentity.renderButton(host, (idToken) => this.onCredential(idToken), {
        theme: dark ? 'filled_black' : 'outline',
        // Google accepts 200-400px; match the form width.
        width: Math.max(200, Math.min(400, available)),
      });
      this.buttonReady.set(true);
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Could not load Google sign-in.');
    }
  }

  private onCredential(idToken: string): void {
    this.signingIn.set(true);
    this.error.set(null);

    this.auth.signInWithGoogle(idToken).subscribe({
      next: () => {
        const returnUrl = this.returnUrl();
        this.router.navigate(['/verify-otp'], { queryParams: returnUrl ? { returnUrl } : {} });
      },
      error: (err: unknown) => {
        this.signingIn.set(false);
        this.error.set(errorMessage(err));
      },
    });
  }
}
