import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';

import { toApiError } from '../errors/api-error';

/**
 * Turns every failed HTTP call into an {@link ApiError} before it reaches a
 * component.
 *
 * Without this, each component would have to decide for itself what a 503
 * means and invent its own wording for it, which is how a UI ends up telling
 * a reviewer "an error occurred" when the server took the trouble to explain
 * that Search is down and to hand over an id support could look up. Doing the
 * translation once, here, means a component's error handler only ever has to
 * decide *where* to show the message, not what it should say.
 *
 * The normalized error keeps its `status`, so interceptors further out — the
 * auth interceptor's 401 handling in particular — keep working unchanged.
 */
export const errorInterceptor: HttpInterceptorFn = (request, next) =>
  next(request).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      const apiError = toApiError(error);

      // Logged with the correlation id so a reviewer's console and the server
      // logs can be lined up when someone reports a problem.
      console.warn(
        `[api] ${request.method} ${request.url} failed with ${apiError.status}` +
          (apiError.correlationId === null ? '' : ` (correlation id ${apiError.correlationId})`),
      );

      return throwError(() => apiError);
    }),
  );
