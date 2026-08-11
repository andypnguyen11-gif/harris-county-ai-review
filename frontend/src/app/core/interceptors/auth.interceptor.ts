import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

import { environment } from '../../../environments/environment';
import { AuthService } from '../auth/auth.service';

/**
 * Attaches the bearer token to API requests and signs the user out when the
 * API rejects it.
 *
 * The token is only ever sent to the configured API origin — an absolute URL
 * to anywhere else (a blob SAS link, say) must not carry our credentials. The
 * sign-in call itself is skipped: it is the request that produces the token.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const isApiRequest = request.url.startsWith(environment.apiUrl);
  const isSignIn = request.url.startsWith(`${environment.apiUrl}/auth/`);
  const token = isApiRequest && !isSignIn ? auth.accessToken() : null;

  const outgoing =
    token === null
      ? request
      : request.clone({ setHeaders: { Authorization: `Bearer ${token}` } });

  return next(outgoing).pipe(
    catchError((error: unknown) => {
      // A 401 means the token is gone or no longer valid; a 403 means the user
      // is signed in but lacks the role, which is not a sign-in problem.
      if (isApiRequest && !isSignIn && isUnauthorized(error)) {
        auth.signOut();
        void router.navigate(['/sign-in']);
      }
      return throwError(() => error);
    }),
  );
};

function isUnauthorized(error: unknown): boolean {
  return typeof error === 'object' && error !== null && (error as { status?: number }).status === 401;
}
