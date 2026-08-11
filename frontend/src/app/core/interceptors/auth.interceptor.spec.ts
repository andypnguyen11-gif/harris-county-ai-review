import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { environment } from '../../../environments/environment';
import { clearSessionStorage, signInStorage } from '../../testing/auth-fixtures';
import { AuthService } from '../auth/auth.service';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let auth: AuthService;

  function configure(): void {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  }

  beforeEach(() => {
    clearSessionStorage();
    TestBed.resetTestingModule();
  });

  afterEach(() => {
    httpMock?.verify();
    clearSessionStorage();
  });

  it('attaches the bearer token to API requests', () => {
    const session = signInStorage();
    configure();

    http.get(`${environment.apiUrl}/cases`).subscribe();

    const req = httpMock.expectOne(`${environment.apiUrl}/cases`);
    expect(req.request.headers.get('Authorization')).toBe(`Bearer ${session.accessToken}`);
    req.flush([]);
  });

  it('sends no Authorization header when signed out', () => {
    configure();

    http.get(`${environment.apiUrl}/cases`).subscribe({ error: () => undefined });

    const req = httpMock.expectOne(`${environment.apiUrl}/cases`);
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush([]);
  });

  it('does not attach the token to the sign-in request', () => {
    signInStorage();
    configure();

    http.post(`${environment.apiUrl}/auth/dev-token`, { username: 'dev.admin' }).subscribe();

    const req = httpMock.expectOne(`${environment.apiUrl}/auth/dev-token`);
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });

  it('does not leak the token to other origins', () => {
    signInStorage();
    configure();

    http.get('https://storage.example.com/blob').subscribe();

    const req = httpMock.expectOne('https://storage.example.com/blob');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });

  it('signs the user out and redirects on a 401 from the API', () => {
    signInStorage();
    configure();
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    http.get(`${environment.apiUrl}/cases`).subscribe({ error: () => undefined });

    httpMock
      .expectOne(`${environment.apiUrl}/cases`)
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(auth.isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/sign-in']);
  });

  it('keeps the session on a 403, which is a role problem rather than a sign-in problem', () => {
    signInStorage();
    configure();
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    http.get(`${environment.apiUrl}/knowledge-base/documents`).subscribe({
      error: () => undefined,
    });

    httpMock
      .expectOne(`${environment.apiUrl}/knowledge-base/documents`)
      .flush(null, { status: 403, statusText: 'Forbidden' });

    expect(auth.isAuthenticated()).toBe(true);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('propagates the error to the caller', () => {
    signInStorage();
    configure();
    let status: number | undefined;

    http
      .get(`${environment.apiUrl}/cases`)
      .subscribe({ error: (error: { status: number }) => (status = error.status) });

    httpMock
      .expectOne(`${environment.apiUrl}/cases`)
      .flush(null, { status: 500, statusText: 'Server Error' });

    expect(status).toBe(500);
  });
});
