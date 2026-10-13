import {
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  input,
  signal,
  viewChildren,
} from '@angular/core';
import { FormArray, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';

import { AuthService } from '../../core/auth/auth.service';
import { safeReturnUrl, toApiProblem } from '../../core/http/api-error';
import { AuthShell } from '../../shared/components/auth-shell/auth-shell';
import { LoadingSpinner } from '../../shared/components/loading-spinner/loading-spinner';
import { NotificationService } from '../../shared/components/toast/notification.service';

const CODE_LENGTH = 6;

@Component({
  selector: 'app-otp-verification',
  imports: [ReactiveFormsModule, AuthShell, LoadingSpinner],
  templateUrl: './otp-verification.html',
  styleUrl: './otp-verification.css',
})
export class OtpVerification {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);

  /** Query param (bound by withComponentInputBinding). */
  readonly returnUrl = input<string>();

  protected readonly challenge = this.auth.pendingChallenge;
  protected readonly digits = new FormArray(
    Array.from(
      { length: CODE_LENGTH },
      () => new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^\d$/)] }),
    ),
  );
  private readonly inputs = viewChildren<ElementRef<HTMLInputElement>>('digit');

  private readonly now = signal(Date.now());
  /** Absolute times, so a page reload keeps the right countdown. */
  private readonly resendAt = signal(0);
  private readonly expiresAt = signal(0);

  protected readonly resendIn = computed(() => secondsUntil(this.resendAt(), this.now()));
  protected readonly expiresIn = computed(() => secondsUntil(this.expiresAt(), this.now()));
  protected readonly expiresLabel = computed(() => formatDuration(this.expiresIn()));

  protected readonly verifying = signal(false);
  protected readonly resending = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly info = signal<string | null>(null);

  constructor() {
    const pending = this.challenge();
    if (pending) {
      this.resendAt.set(pending.receivedAt + pending.resendAvailableInSeconds * 1000);
      this.expiresAt.set(pending.receivedAt + pending.expiresInSeconds * 1000);
      this.info.set(
        pending.codeSent
          ? `We sent a 6-digit code to ${pending.maskedEmail}.`
          : `We already sent a code to ${pending.maskedEmail}. Check your inbox (and spam folder).`,
      );
    }

    const timer = setInterval(() => this.now.set(Date.now()), 1000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));

    afterNextRender(() => this.focus(0));
  }

  protected onInput(index: number, event: Event): void {
    const element = event.target as HTMLInputElement;
    const typed = element.value.replace(/\D/g, '');

    if (typed.length > 1) {
      // Autofill or very fast typing put several digits into one box.
      this.fillFrom(index, typed);
      return;
    }

    this.digits.at(index).setValue(typed);
    element.value = typed;
    this.error.set(null);

    if (typed && index < CODE_LENGTH - 1) {
      this.focus(index + 1);
    }
    this.submitIfComplete();
  }

  protected onKeydown(index: number, event: KeyboardEvent): void {
    if (event.key === 'Backspace' && !this.digits.at(index).value && index > 0) {
      event.preventDefault();
      this.digits.at(index - 1).setValue('');
      this.focus(index - 1);
    } else if (event.key === 'ArrowLeft' && index > 0) {
      event.preventDefault();
      this.focus(index - 1);
    } else if (event.key === 'ArrowRight' && index < CODE_LENGTH - 1) {
      event.preventDefault();
      this.focus(index + 1);
    } else if (event.key === 'Enter') {
      event.preventDefault();
      this.verify();
    }
  }

  protected onPaste(index: number, event: ClipboardEvent): void {
    const pasted = (event.clipboardData?.getData('text') ?? '').replace(/\D/g, '');
    if (pasted) {
      event.preventDefault();
      this.fillFrom(index, pasted);
    }
  }

  protected verify(): void {
    if (this.verifying() || this.digits.invalid) {
      if (this.digits.invalid) {
        this.error.set('Enter all 6 digits of the code.');
      }
      return;
    }

    this.verifying.set(true);
    this.error.set(null);

    this.auth.verifyOtp(this.digits.getRawValue().join('')).subscribe({
      next: (user) => {
        this.notifications.success(`Welcome, ${user.fullName.split(' ')[0]}!`);
        this.router.navigateByUrl(safeReturnUrl(this.returnUrl()));
      },
      error: (err: unknown) => {
        this.verifying.set(false);
        const problem = toApiProblem(err);

        if (problem.status === 401) {
          this.restartSignIn(problem.detail ?? 'Your verification session has expired. Please sign in again.');
          return;
        }

        this.error.set(problem.detail ?? 'That code did not work. Please try again.');
        if (problem.reason === 'expired' || problem.reason === 'too_many_attempts') {
          // The code is dead; a new one is required, so allow resending right away.
          this.resendAt.set(Math.min(this.resendAt(), Date.now()));
        }
        this.clearDigits();
      },
    });
  }

  protected resend(): void {
    if (this.resending() || this.resendIn() > 0) {
      return;
    }

    this.resending.set(true);
    this.error.set(null);

    this.auth.resendOtp().subscribe({
      next: (challenge) => {
        this.resending.set(false);
        this.resendAt.set(Date.now() + challenge.resendAvailableInSeconds * 1000);
        this.expiresAt.set(Date.now() + challenge.expiresInSeconds * 1000);
        this.info.set(`We sent a new code to ${challenge.maskedEmail}. Previous codes no longer work.`);
        this.clearDigits();
      },
      error: (err: unknown) => {
        this.resending.set(false);
        const problem = toApiProblem(err);

        if (problem.status === 401) {
          this.restartSignIn(problem.detail ?? 'Your verification session has expired. Please sign in again.');
          return;
        }
        if (problem.retryAfterSeconds) {
          this.resendAt.set(Date.now() + problem.retryAfterSeconds * 1000);
        }
        this.error.set(problem.detail ?? 'We could not send a new code. Please try again.');
      },
    });
  }

  protected useDifferentAccount(): void {
    this.auth.clearChallenge();
    this.router.navigate(['/login']);
  }

  private restartSignIn(message: string): void {
    this.notifications.error(message);
    this.auth.clearChallenge();
    this.router.navigate(['/login']);
  }

  private fillFrom(start: number, value: string): void {
    const chars = value.slice(0, CODE_LENGTH - start).split('');
    chars.forEach((char, offset) => this.digits.at(start + offset).setValue(char));
    this.inputs().forEach((ref, i) => (ref.nativeElement.value = this.digits.at(i).value));
    this.error.set(null);
    this.focus(Math.min(start + chars.length, CODE_LENGTH - 1));
    this.submitIfComplete();
  }

  private clearDigits(): void {
    this.digits.reset();
    this.inputs().forEach((ref) => (ref.nativeElement.value = ''));
    this.focus(0);
  }

  private submitIfComplete(): void {
    if (this.digits.valid) {
      this.verify();
    }
  }

  private focus(index: number): void {
    const element = this.inputs()[index]?.nativeElement;
    element?.focus();
    element?.select();
  }
}

function secondsUntil(target: number, now: number): number {
  return Math.max(0, Math.ceil((target - now) / 1000));
}

function formatDuration(totalSeconds: number): string {
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${seconds.toString().padStart(2, '0')}`;
}
