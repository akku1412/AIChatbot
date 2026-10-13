/** Mirrors ChatGPTClone.Api DTOs (camelCase JSON). */

export interface User {
  userId: number;
  fullName: string;
  email: string;
  profileImageUrl: string | null;
  isEmailVerified: boolean;
}

/** Returned by POST /api/auth/google and /api/auth/send-otp. */
export interface OtpChallenge {
  challengeToken: string;
  maskedEmail: string;
  codeSent: boolean;
  expiresInSeconds: number;
  resendAvailableInSeconds: number;
}

/** Returned by POST /api/auth/verify-otp and /api/auth/refresh. The refresh token is an HttpOnly cookie. */
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
