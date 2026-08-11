import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../environments/environment';
import { clearSessionStorage, makeSession, signInStorage } from '../../testing/auth-fixtures';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let httpMock: HttpTestingController;

  function configure(): AuthService {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
    return service;
  }

  beforeEach(() => {
    clearSessionStorage();
    TestBed.resetTestingModule();
  });

  afterEach(() => {
    httpMock?.verify();
    clearSessionStorage();
  });

  it('starts signed out when nothing is stored', () => {
    const service = configure();

    expect(service.isAuthenticated()).toBe(false);
    expect(service.accessToken()).toBeNull();
    expect(service.user()).toBeNull();
  });

  it('signIn posts the username to the dev-token endpoint and stores the session', () => {
    const service = configure();
    const session = makeSession();

    service.signIn('dev.reviewer').subscribe();

    const req = httpMock.expectOne(`${environment.apiUrl}/auth/dev-token`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ username: 'dev.reviewer' });
    req.flush(session);

    expect(service.isAuthenticated()).toBe(true);
    expect(service.accessToken()).toBe(session.accessToken);
    expect(service.user()).toEqual({
      username: session.username,
      displayName: session.displayName,
      roles: session.roles,
    });
    expect(sessionStorage.getItem(AuthService.storageKey)).toBe(JSON.stringify(session));
  });

  it('restores a stored session so a page reload stays signed in', () => {
    const stored = signInStorage();
    const service = configure();

    expect(service.isAuthenticated()).toBe(true);
    expect(service.accessToken()).toBe(stored.accessToken);
  });

  it('discards a stored session whose token has already expired', () => {
    signInStorage({ expiresAt: new Date(Date.now() - 1000).toISOString() });
    const service = configure();

    expect(service.isAuthenticated()).toBe(false);
    expect(sessionStorage.getItem(AuthService.storageKey)).toBeNull();
  });

  it('discards unparseable stored data instead of throwing', () => {
    sessionStorage.setItem(AuthService.storageKey, 'not-json');
    const service = configure();

    expect(service.isAuthenticated()).toBe(false);
    expect(sessionStorage.getItem(AuthService.storageKey)).toBeNull();
  });

  it('signOut clears the session and the stored copy', () => {
    signInStorage();
    const service = configure();

    service.signOut();

    expect(service.isAuthenticated()).toBe(false);
    expect(service.accessToken()).toBeNull();
    expect(sessionStorage.getItem(AuthService.storageKey)).toBeNull();
  });

  it('hasRole reports roles from the session and treats Administrator separately', () => {
    signInStorage({ roles: ['Administrator'] });
    const service = configure();

    expect(service.hasRole('Administrator')).toBe(true);
    expect(service.hasRole('Reviewer')).toBe(false);
    expect(service.hasRole('Reviewer', 'Administrator')).toBe(true);
  });

  it('hasRole is false when signed out', () => {
    const service = configure();

    expect(service.hasRole('Reviewer')).toBe(false);
  });

  it('does not store a session when sign-in fails', () => {
    const service = configure();
    let failed = false;

    service.signIn('nobody').subscribe({ error: () => (failed = true) });

    httpMock
      .expectOne(`${environment.apiUrl}/auth/dev-token`)
      .flush({ title: 'Bad Request' }, { status: 400, statusText: 'Bad Request' });

    expect(failed).toBe(true);
    expect(service.isAuthenticated()).toBe(false);
    expect(sessionStorage.getItem(AuthService.storageKey)).toBeNull();
  });
});
