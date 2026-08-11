import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { AuthService } from '../auth/auth.service';

/**
 * Keeps unauthenticated users off application routes, sending them to sign-in
 * with the route they wanted so they land there afterwards.
 *
 * This is a usability guard, not the security boundary — the API enforces
 * every policy server-side regardless of what the client allows.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.accessToken() !== null) {
    return true;
  }

  return router.createUrlTree(['/sign-in'], { queryParams: { returnUrl: state.url } });
};
