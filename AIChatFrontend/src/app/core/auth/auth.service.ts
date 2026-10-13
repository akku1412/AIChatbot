import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, finalize, map, of, shareReplay, tap } from 'rxjs';

import { environment } from '../../../environments/environment';
import { AuthResponse, OtpChallenge, User } from './auth.models';
import { GoogleIdentityService } from './google-identity.service';

const CHALLENGE_KEY = 'aichat.otpChallenge';

/** An OTP challenge plus when this browser received it, so countdowns survive a page reload. */
export interface PendingChallenge extends OtpChallenge {
  receivedAt: number;
}

/**
 * Session state. The access token lives in memory only (never localStorage), so a page reload
 * restores the session through POST /auth/refresh, which uses the HttpOnly refresh cookie.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly googleIdentity = inject(GoogleIdentityService);
  private readonly baseUrl = `${environment.apiBaseUrl}/auth`;

  private readonly currentUser = signal<User | null>(null);
  private readonly challenge = signal<PendingChallenge | null>(readStoredChallenge());
  private accessToken: string | null = null;
  private accessTokenExpiresAt = 0;
  private refreshInFlight: Observable<string | null> | null = null;

  readonly user = this.currentUser.asReadonly();
  readonly isAuthenticated = computed(() => this.currentUser() !== null);
  /** Pending OTP verification (set after Google sign-in, cleared after verify). */
  readonly pendingChallenge = this.challenge.asReadonly();

  getAccessToken(): string | null {
    return this.accessToken;
  }

  /** Step 1: exchange the Google credential for an OTP challenge; a code is emailed. */
  signInWithGoogle(idToken: string): Observable<OtpChallenge> {
    return this.http
      .post<OtpChallenge>(`${this.baseUrl}/google`, { idToken })
      .pipe(tap((challenge) => this.setChallenge(challenge)));
  }

  resendOtp(): Observable<OtpChallenge> {
    return this.http
      .post<OtpChallenge>(`${this.baseUrl}/send-otp`, { challengeToken: this.requireChallenge() })
      .pipe(tap((challenge) => this.setChallenge(challenge)));
  }

  /** Step 2: verify the code. Only this starts a session. */
  verifyOtp(code: string): Observable<User> {
    return this.http
      .post<AuthResponse>(`${this.baseUrl}/verify-otp`, { challengeToken: this.requireChallenge(), code })
      .pipe(
        tap((response) => {
          this.setSession(response);
          this.setChallenge(null);
        }),
        map((response) => response.user),
      );
  }

  /** Abandons the pending verification (e.g. "use a different account"). */
  clearChallenge(): void {
    this.setChallenge(null);
  }

  /**
   * Gets a new access token from the refresh cookie. Concurrent callers share one request,
   * because the server rotates the refresh token on every call. Emits null when signed out.
   */
  refresh(): Observable<string | null> {
    this.refreshInFlight ??= this.http.post<AuthResponse>(`${this.baseUrl}/refresh`, {}).pipe(
      map((response) => {
        this.setSession(response);
        return response.accessToken;
      }),
      catchError(() => {
        this.clearSession();
        return of(null);
      }),
      finalize(() => (this.refreshInFlight = null)),
      shareReplay(1),
    );
    return this.refreshInFlight;
  }

  /** True when there is a usable access token, refreshing silently if needed (used by route guards). */
  ensureSession(): Observable<boolean> {
    if (this.accessToken && Date.now() < this.accessTokenExpiresAt - 30_000) {
      return of(true);
    }
    return this.refresh().pipe(map((token) => token !== null));
  }

  /** Updates the cached profile after PUT /users/me. */
  updateUser(user: User): void {
    this.currentUser.set(user);
  }

  logout(): void {
    this.http
      .post<void>(`${this.baseUrl}/logout`, {})
      .pipe(
        catchError(() => of(undefined)),
        finalize(() => {
          this.clearSession();
          this.googleIdentity.disableAutoSelect();
          this.router.navigateByUrl('/login');
        }),
      )
      .subscribe();
  }

  /** Called when a refresh fails mid-session: drop local state and go to the login page. */
  handleSessionExpired(): void {
    this.clearSession();
    this.router.navigate(['/login'], { queryParams: { expired: 1 } });
  }

  private setSession(response: AuthResponse): void {
    this.accessToken = response.accessToken;
    this.accessTokenExpiresAt = Date.parse(response.accessTokenExpiresAt);
    this.currentUser.set(response.user);
  }

  private clearSession(): void {
    this.accessToken = null;
    this.accessTokenExpiresAt = 0;
    this.currentUser.set(null);
  }

  private setChallenge(challenge: OtpChallenge | null): void {
    const pending = challenge ? { ...challenge, receivedAt: Date.now() } : null;
    this.challenge.set(pending);
    try {
      // sessionStorage (per tab, cleared on close) so a reload on the OTP page keeps the pending
      // verification. The challenge token only works on the OTP endpoints and expires in minutes.
      if (pending) {
        sessionStorage.setItem(CHALLENGE_KEY, JSON.stringify(pending));
      } else {
        sessionStorage.removeItem(CHALLENGE_KEY);
      }
    } catch {
      // Storage unavailable (private mode etc.): the in-memory signal still works.
    }
  }

  private requireChallenge(): string {
    const challenge = this.challenge();
    if (!challenge) {
      throw new Error('No pending verification. Sign in with Google first.');
    }
    return challenge.challengeToken;
  }
}

function readStoredChallenge(): PendingChallenge | null {
  try {
    const raw = sessionStorage.getItem(CHALLENGE_KEY);
    return raw ? (JSON.parse(raw) as PendingChallenge) : null;
  } catch {
    return null;
  }
}
