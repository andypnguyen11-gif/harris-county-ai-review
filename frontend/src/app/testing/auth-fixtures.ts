import { AuthSession } from '../core/auth/auth-session.model';
import { AuthService } from '../core/auth/auth.service';

export function makeSession(overrides: Partial<AuthSession> = {}): AuthSession {
  return {
    accessToken: 'test-access-token',
    tokenType: 'Bearer',
    // Far enough out that a test never races the expiry check.
    expiresAt: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
    username: 'dev.reviewer',
    displayName: 'Dev Reviewer',
    roles: ['Reviewer'],
    ...overrides,
  };
}

/**
 * Seeds a stored session so an injected AuthService starts signed in. Call
 * before the first TestBed.inject(AuthService), since the service reads storage
 * when it is constructed.
 */
export function signInStorage(overrides: Partial<AuthSession> = {}): AuthSession {
  const session = makeSession(overrides);
  sessionStorage.setItem(AuthService.storageKey, JSON.stringify(session));
  return session;
}

export function clearSessionStorage(): void {
  sessionStorage.removeItem(AuthService.storageKey);
}
