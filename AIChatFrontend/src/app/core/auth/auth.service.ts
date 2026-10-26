import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, finalize, map, of, shareReplay, tap } from 'rxjs';

import { environment } from '../../../environments/environment';
import { AuthResponse, OtpSent, User } from './auth.models';
import { GoogleIdentityService } from './google-identity.service';

const PENDING_KEY = 'aichat.pendingOtp';

/** An emailed code waiting to be verified, plus when this browser got it (so countdowns survive a reload). */
export interface PendingOtp extends OtpSent {
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
  private readonly pending = signal<PendingOtp | null>(readStoredPending());
  private accessToken: string | null = null;
  private accessTokenExpiresAt = 0;
  private refreshInFlight: Observable<string | null> | null = null;

  readonly user = this.currentUser.asReadonly();
  readonly isAuthenticated = computed(() => this.currentUser() !== null);
  /** Email sign-in waiting for its code (set by sendOtp, cleared after verify). */
  readonly pendingOtp = this.pending.asReadonly();

  getAccessToken(): string | null {
    return this.accessToken;
  }

  // ---------- Email OTP ----------

  /** Step 1: email a 6-digit code to the address. */
  sendOtp(email: string): Observable<OtpSent> {
    return this.http
      .post<OtpSent>(`${this.baseUrl}/send-otp`, { email: email.trim() })
      .pipe(tap((sent) => this.setPending(sent)));
  }

  /** New code after the cooldown; the previous code stops working. */
  resendOtp(): Observable<OtpSent> {
    return this.http
      .post<OtpSent>(`${this.baseUrl}/resend-otp`, { email: this.requirePendingEmail() })
      .pipe(tap((sent) => this.setPending(sent)));
  }

  /** Step 2: verify the code; creates the account on first sign-in and starts the session. */
  verifyOtp(code: string): Observable<User> {
    return this.http
      .post<AuthResponse>(`${this.baseUrl}/verify-otp`, { email: this.requirePendingEmail(), code })
      .pipe(
        tap((response) => {
          this.setSession(response);
          this.setPending(null);
        }),
        map((response) => response.user),
      );
  }

  /** Abandons the pending email sign-in ("change email"). */
  clearPendingOtp(): void {
    this.setPending(null);
  }

  // ---------- Google ----------

  /** Google sign-in: the API validates the Google ID token and starts the session directly (no OTP). */
  signInWithGoogle(idToken: string): Observable<User> {
    return this.http.post<AuthResponse>(`${this.baseUrl}/google`, { idToken }).pipe(
      tap((response) => {
        this.setSession(response);
        this.setPending(null);
      }),
      map((response) => response.user),
    );
  }

  // ---------- Session ----------

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

  /** An admin blocked this account: end the session (the server already revoked it) and explain on the login page. */
  handleBlocked(): void {
    if (!this.currentUser() && !this.accessToken) {
      return; // several requests can fail at once; only the first one navigates
    }
    this.clearSession();
    this.router.navigate(['/login'], { queryParams: { blocked: 1 } });
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

  private setPending(sent: OtpSent | null): void {
    const pending = sent ? { ...sent, receivedAt: Date.now() } : null;
    this.pending.set(pending);
    try {
      // sessionStorage (this tab only, cleared on close) so a reload on the code page keeps the email.
      // It holds no secret: only the address and countdown times.
      if (pending) {
        sessionStorage.setItem(PENDING_KEY, JSON.stringify(pending));
      } else {
        sessionStorage.removeItem(PENDING_KEY);
      }
    } catch {
      // Storage unavailable (private mode etc.): the in-memory signal still works.
    }
  }

  private requirePendingEmail(): string {
    const pending = this.pending();
    if (!pending) {
      throw new Error('No pending email sign-in. Enter your email first.');
    }
    return pending.email;
  }
}

function readStoredPending(): PendingOtp | null {
  try {
    const raw = sessionStorage.getItem(PENDING_KEY);
    return raw ? (JSON.parse(raw) as PendingOtp) : null;
  } catch {
    return null;
  }
}
