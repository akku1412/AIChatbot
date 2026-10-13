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

/** Login/OTP pages: a signed-in user is sent straight to the chat. */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.ensureSession().pipe(map((ok) => (ok ? router.createUrlTree(['/chat']) : true)));
};
