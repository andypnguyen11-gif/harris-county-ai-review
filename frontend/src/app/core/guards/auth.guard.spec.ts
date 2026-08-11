import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';

import { clearSessionStorage, signInStorage } from '../../testing/auth-fixtures';
import { authGuard } from './auth.guard';

describe('authGuard', () => {
  function configure(): void {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
  }

  function run(url: string) {
    const state = { url } as RouterStateSnapshot;
    return TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, state),
    );
  }

  beforeEach(() => {
    clearSessionStorage();
    TestBed.resetTestingModule();
  });

  afterEach(() => clearSessionStorage());

  it('allows an authenticated user through', () => {
    signInStorage();
    configure();

    expect(run('/cases')).toBe(true);
  });

  it('redirects an anonymous user to sign-in, remembering where they were going', () => {
    configure();

    const result = run('/cases/abc');

    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe('/sign-in?returnUrl=%2Fcases%2Fabc');
  });

  it('redirects when the stored token has expired', () => {
    signInStorage({ expiresAt: new Date(Date.now() - 1000).toISOString() });
    configure();

    expect(run('/cases')).toBeInstanceOf(UrlTree);
  });
});
