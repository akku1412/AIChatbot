import { HttpErrorResponse } from '@angular/common/http';
import { MonoTypeOperatorFunction, catchError, from, switchMap, throwError } from 'rxjs';

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

  const body = error.error;
  // The API's own ProblemDetails (including AI errors sent as 502/503/504) always wins.
  if (body && typeof body === 'object' && ('title' in body || 'detail' in body || 'errors' in body)) {
    return { ...(body as ApiProblem), status: error.status };
  }

  // 0 = network/CORS failure; a bare 502-504 = the dev proxy or a gateway could not reach the API.
  if (error.status === 0 || (error.status >= 502 && error.status <= 504)) {
    return { ...NETWORK_PROBLEM, status: error.status };
  }

  return { status: error.status, title: 'Something went wrong', detail: 'Please try again in a moment.' };
}

/** The single best user-facing sentence for an error. */
export function errorMessage(error: unknown): string {
  const problem = toApiProblem(error);
  const firstValidationError = problem.errors ? Object.values(problem.errors).flat()[0] : undefined;
  return firstValidationError ?? problem.detail ?? problem.title ?? 'Something went wrong. Please try again.';
}

/**
 * File downloads use responseType 'blob' (and streams 'text'), so a failure's ProblemDetails arrives as a Blob
 * or a string too. This operator parses it back into JSON, so errorMessage() shows the server's explanation.
 */
export function readBlobErrors<T>(): MonoTypeOperatorFunction<T> {
  return catchError((error: unknown) => from(withJsonErrorBody(error)).pipe(switchMap((e) => throwError(() => e))));
}

async function withJsonErrorBody(error: unknown): Promise<unknown> {
  if (!(error instanceof HttpErrorResponse) || !(error.error instanceof Blob || typeof error.error === 'string')) {
    return error;
  }
  try {
    const body: unknown = JSON.parse(typeof error.error === 'string' ? error.error : await blobText(error.error));
    return new HttpErrorResponse({
      error: body,
      status: error.status,
      statusText: error.statusText,
      headers: error.headers,
      url: error.url ?? undefined,
    });
  } catch {
    return error; // not JSON (e.g. a proxy's HTML error page): keep the generic message
  }
}

function blobText(blob: Blob): Promise<string> {
  if (typeof blob.text === 'function') {
    return blob.text();
  }
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(reader.error);
    reader.readAsText(blob);
  });
}

/** Allows only same-app relative paths, so ?returnUrl= cannot redirect to another site. */
export function safeReturnUrl(url: string | null | undefined, fallback = '/chat'): string {
  return url && url.startsWith('/') && !url.startsWith('//') && !url.startsWith('/\\') ? url : fallback;
}
