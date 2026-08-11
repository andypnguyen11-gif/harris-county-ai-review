import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { ApplicationRole } from '../auth/application-roles';
import { AuthService } from '../auth/auth.service';

/**
 * Builds a guard that admits a user holding any one of the given roles.
 *
 * Like {@link authGuard} this only shapes navigation — the API rejects a
 * request the user is not entitled to make regardless of which routes the
 * client was willing to open. An authenticated user without the role is sent
 * to the dashboard rather than to sign-in: signing in again would not help.
 */
export function requireRole(...roles: ApplicationRole[]): CanActivateFn {
  return (_route, state) => {
    const auth = inject(AuthService);
    const router = inject(Router);

    if (auth.accessToken() === null) {
      return router.createUrlTree(['/sign-in'], { queryParams: { returnUrl: state.url } });
    }

    return auth.hasRole(...roles) ? true : router.createUrlTree(['/']);
  };
}
