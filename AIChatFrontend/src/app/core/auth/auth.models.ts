/** Mirrors ChatGPTClone.Api DTOs (camelCase JSON). */

export interface User {
  userId: number;
  fullName: string;
  email: string;
  profileImageUrl: string | null;
  isEmailVerified: boolean;
  /** True when a Google account is linked (signed in with Google at least once). */
  hasGoogleAccount: boolean;
  createdAt: string;
}

/** Returned by POST /api/auth/send-otp and /api/auth/resend-otp. */
export interface OtpSent {
  email: string;
  codeSent: boolean;
  expiresInSeconds: number;
  resendAvailableInSeconds: number;
  message: string;
}

/** Returned by verify-otp, google and refresh. The refresh token is an HttpOnly cookie, never in the body. */
export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  user: User;
}

/** RFC 7807 error body produced by the API. */
export interface ApiProblem {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
  retryAfterSeconds?: number;
  attemptsRemaining?: number;
  reason?: string;
}
