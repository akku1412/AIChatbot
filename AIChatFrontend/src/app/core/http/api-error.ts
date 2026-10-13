import { HttpErrorResponse } from '@angular/common/http';

import { ApiProblem } from '../auth/auth.models';

export type ResolvedProblem = ApiProblem & { status: number };

const NETWORK_PROBLEM: ResolvedProblem = {
  status: 0,
  title: 'Connection problem',
  detail: "We can't reach the server right now. Check your connection and try again.",
};

/** Normalizes any HTTP failure into the API's ProblemDetails shape. */
export function toApiProblem(error: unknown): ResolvedProblem {
  if (!(error instanceof HttpErrorResponse)) {
    return { status: -1, title: 'Something went wrong', detail: 'Please try again.' };
  }

  // 0 = network/CORS failure; 502-504 = the dev proxy or a gateway could not reach the API.
  if (error.status === 0 || (error.status >= 502 && error.status <= 504)) {
    return { ...NETWORK_PROBLEM, status: error.status };
  }

  const body = error.error;
  if (body && typeof body === 'object') {
    return { ...(body as ApiProblem), status: error.status };
  }

  return { status: error.status, title: 'Something went wrong', detail: 'Please try again in a moment.' };
}

/** The single best user-facing sentence for an error. */
export function errorMessage(error: unknown): string {
  const problem = toApiProblem(error);
  const firstValidationError = problem.errors ? Object.values(problem.errors).flat()[0] : undefined;
  return firstValidationError ?? problem.detail ?? problem.title ?? 'Something went wrong. Please try again.';
}

/** Allows only same-app relative paths, so ?returnUrl= cannot redirect to another site. */
export function safeReturnUrl(url: string | null | undefined, fallback = '/chat'): string {
  return url && url.startsWith('/') && !url.startsWith('//') && !url.startsWith('/\\') ? url : fallback;
}
