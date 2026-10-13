import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';

import { environment } from '../../../environments/environment';
import { AuthService } from '../auth/auth.service';

/**
 * Adds "Authorization: Bearer <token>" to API calls. On a 401 it refreshes once (shared across
 * concurrent requests) and retries; if the refresh fails the user is sent to the login page.
 * Auth endpoints are skipped: they use the OTP challenge or the refresh cookie instead.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const apiBase = environment.apiBaseUrl;
  if (!req.url.startsWith(apiBase)) {
    return next(req);
  }

  const auth = inject(AuthService);
  const isAuthEndpoint = req.url.startsWith(`${apiBase}/auth/`);
  const token = auth.getAccessToken();

  return next(token && !isAuthEndpoint ? withBearer(req, token) : req).pipe(
    catchError((error: unknown) => {
      if (isAuthEndpoint || !(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }

      return auth.refresh().pipe(
        switchMap((newToken) => {
          if (!newToken) {
            auth.handleSessionExpired();
            return throwError(() => error);
          }
          // Retried once; a second 401 propagates to the caller.
          return next(withBearer(req, newToken));
        }),
      );
    }),
  );
};

function withBearer(req: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
  return req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
}
