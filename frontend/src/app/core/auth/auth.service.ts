import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

import { environment } from '../../../environments/environment';
import { AuthSession } from './auth-session.model';

/**
 * Holds the current session and is the only place the access token is read
 * from. Sign-in currently goes through the API's local-development token
 * endpoint; switching to Entra ID replaces `signIn` and leaves the rest of the
 * application — interceptor, guards, components — untouched.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  /** Survives a page reload but not a closed tab, which suits a review console. */
  static readonly storageKey = 'harris-county-ai.session';

  private readonly http = inject(HttpClient);
  private readonly sessionSignal = signal<AuthSession | null>(readStoredSession());

  readonly session = this.sessionSignal.asReadonly();
  readonly user = computed(() => {
    const session = this.sessionSignal();
    return session === null
      ? null
      : { username: session.username, displayName: session.displayName, roles: session.roles };
  });
  readonly isAuthenticated = computed(() => this.sessionSignal() !== null);

  /** The bearer token, or null when signed out or the stored token has expired. */
  accessToken(): string | null {
    const session = this.sessionSignal();
    if (session === null) {
      return null;
    }

    if (hasExpired(session)) {
      this.signOut();
      return null;
    }

    return session.accessToken;
  }

  hasRole(...roles: string[]): boolean {
    const session = this.sessionSignal();
    return session !== null && roles.some((role) => session.roles.includes(role));
  }

  signIn(username: string): Observable<AuthSession> {
    return this.http
      .post<AuthSession>(`${environment.apiUrl}/auth/dev-token`, { username })
      .pipe(tap((session) => this.store(session)));
  }

  signOut(): void {
    this.sessionSignal.set(null);
    sessionStorage.removeItem(AuthService.storageKey);
  }

  private store(session: AuthSession): void {
    this.sessionSignal.set(session);
    sessionStorage.setItem(AuthService.storageKey, JSON.stringify(session));
  }
}

function hasExpired(session: AuthSession): boolean {
  const expiresAt = Date.parse(session.expiresAt);
  return Number.isNaN(expiresAt) || expiresAt <= Date.now();
}

function readStoredSession(): AuthSession | null {
  const raw = sessionStorage.getItem(AuthService.storageKey);
  if (raw === null) {
    return null;
  }

  try {
    const session = JSON.parse(raw) as AuthSession;
    // A stored token that is already expired is worth nothing; drop it now
    // rather than letting the first API call fail with a 401.
    if (typeof session?.accessToken !== 'string' || hasExpired(session)) {
      sessionStorage.removeItem(AuthService.storageKey);
      return null;
    }
    return session;
  } catch {
    sessionStorage.removeItem(AuthService.storageKey);
    return null;
  }
}
