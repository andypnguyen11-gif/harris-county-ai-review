import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { environment } from '../../../environments/environment';
import { clearSessionStorage, signInStorage } from '../../testing/auth-fixtures';
import { ApiError } from '../errors/api-error';
import { AuthService } from '../auth/auth.service';
import { authInterceptor } from './auth.interceptor';
import { errorInterceptor } from './error.interceptor';

describe('errorInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  function configure(): void {
    TestBed.configureTestingModule({
      providers: [
        // Same order as the application: the error interceptor is inner, so it
        // normalizes the failure before the auth interceptor inspects it.
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  }

  beforeEach(() => {
    clearSessionStorage();
    TestBed.resetTestingModule();
    vi.spyOn(console, 'warn').mockImplementation(() => undefined);
  });

  afterEach(() => {
    httpMock?.verify();
    clearSessionStorage();
    vi.restoreAllMocks();
  });

  function failWith(status: number, body: Record<string, unknown> | null): ApiError {
    let captured: ApiError | undefined;
    http.get(`${environment.apiUrl}/questions`).subscribe({
      error: (error: ApiError) => (captured = error),
    });
    httpMock
      .expectOne(`${environment.apiUrl}/questions`)
      .flush(body, { status, statusText: 'Error' });
    expect(captured).toBeDefined();
    return captured!;
  }

  it('hands the component a normalized error instead of a raw HTTP response', () => {
    configure();

    const error = failWith(503, {
      detail: 'The Search service could not be reached, so this feature is unavailable right now.',
      service: 'Search',
      correlationId: 'corr-1',
    });

    expect(error.kind).toBe('dependency');
    expect(error.service).toBe('Search');
    expect(error.correlationId).toBe('corr-1');
    expect(error.message).toContain('Search');
  });

  it('logs the correlation id so the console and the server logs line up', () => {
    configure();

    failWith(500, { correlationId: 'corr-2' });

    expect(console.warn).toHaveBeenCalledWith(expect.stringContaining('corr-2'));
  });

  it('leaves a successful response untouched', () => {
    configure();
    let body: unknown;

    http.get(`${environment.apiUrl}/cases`).subscribe((response) => (body = response));
    httpMock.expectOne(`${environment.apiUrl}/cases`).flush([{ id: '1' }]);

    expect(body).toEqual([{ id: '1' }]);
  });

  it('keeps the status, so the auth interceptor still signs the user out on a 401', () => {
    signInStorage();
    configure();
    const auth = TestBed.inject(AuthService);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    const error = failWith(401, null);

    expect(error.status).toBe(401);
    expect(auth.isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/sign-in']);
  });

  it('does not disturb an error that is not an HTTP response', () => {
    configure();
    const thrown = new Error('client-side failure');
    let captured: unknown;

    http
      .get(`${environment.apiUrl}/cases`)
      .subscribe({ error: (error: unknown) => (captured = error) });
    httpMock.expectOne(`${environment.apiUrl}/cases`).error(thrown as unknown as ProgressEvent);

    // Angular wraps transport errors in an HttpErrorResponse, so this still
    // arrives normalized — with the offline classification a dead API gets.
    expect((captured as ApiError).kind).toBe('offline');
  });
});
