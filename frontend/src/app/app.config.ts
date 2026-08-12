import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';

import { authInterceptor } from './core/interceptors/auth.interceptor';
import { errorInterceptor } from './core/interceptors/error.interceptor';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    // Order matters on the way back: the error interceptor is inner, so it
    // normalizes the failure first and the auth interceptor then sees an
    // ApiError that still carries its status.
    provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
  ],
};
