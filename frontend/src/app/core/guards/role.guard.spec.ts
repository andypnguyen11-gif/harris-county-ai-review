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
import { ApplicationRoles } from '../auth/application-roles';
import { requireRole } from './role.guard';

describe('requireRole', () => {
  function configure(): void {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
  }

  function run(url: string, ...roles: string[]) {
    const state = { url } as RouterStateSnapshot;
    return TestBed.runInInjectionContext(() =>
      requireRole(...(roles as (typeof ApplicationRoles)[keyof typeof ApplicationRoles][]))(
        {} as ActivatedRouteSnapshot,
        state,
      ),
    );
  }

  beforeEach(() => {
    clearSessionStorage();
    TestBed.resetTestingModule();
  });

  afterEach(() => clearSessionStorage());

  it('admits a user holding the required role', () => {
    signInStorage({ roles: ['Administrator'] });
    configure();

    expect(run('/knowledge-base', ApplicationRoles.Administrator)).toBe(true);
  });

  it('sends an authenticated user without the role to the dashboard, not to sign-in', () => {
    signInStorage({ roles: ['Reviewer'] });
    configure();

    const result = run('/knowledge-base', ApplicationRoles.Administrator);

    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe('/');
  });

  it('sends an anonymous user to sign-in with the return url', () => {
    configure();

    const result = run('/knowledge-base', ApplicationRoles.Administrator);

    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe('/sign-in?returnUrl=%2Fknowledge-base');
  });

  it('admits a user holding any one of several accepted roles', () => {
    signInStorage({ roles: ['Administrator'] });
    configure();

    expect(run('/cases', ApplicationRoles.Reviewer, ApplicationRoles.Administrator)).toBe(true);
  });

  it('rejects a user whose token carries no roles', () => {
    signInStorage({ roles: [] });
    configure();

    expect(run('/knowledge-base', ApplicationRoles.Administrator)).toBeInstanceOf(UrlTree);
  });
});
