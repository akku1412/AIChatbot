import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';

import { AuthService } from '../auth/auth.service';

/** Protected pages: requires a session (restoring it from the refresh cookie after a reload). */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth
    .ensureSession()
    .pipe(map((ok) => ok || router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } })));
};

/** Admin pages (inside the signed-in area, so the session is already restored): other users go to the chat. */
export const adminGuard: CanActivateFn = () =>
  inject(AuthService).user()?.role === 'Admin' || inject(Router).createUrlTree(['/chat']);

/** Code page: only reachable while an email sign-in is waiting for its code. */
export const otpChallengeGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.pendingOtp() ? true : inject(Router).createUrlTree(['/login']);
};

/** Login/OTP pages: a signed-in user is sent straight to the chat. */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.ensureSession().pipe(map((ok) => (ok ? router.createUrlTree(['/chat']) : true)));
};
