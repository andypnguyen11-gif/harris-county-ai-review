import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';

import { isApiError, toApiError } from './api-error';

describe('toApiError', () => {
  function problemResponse(
    status: number,
    body: Record<string, unknown> | null,
    headers?: HttpHeaders,
  ): HttpErrorResponse {
    return new HttpErrorResponse({
      status,
      statusText: 'Error',
      error: body,
      headers,
      url: '/api/questions',
    });
  }

  it('reads the server’s explanation, the failed service, and the reference id', () => {
    const error = toApiError(
      problemResponse(503, {
        title: 'A required service is temporarily unavailable.',
        detail:
          'The Search service could not be reached, so this feature is unavailable right now.',
        service: 'Search',
        correlationId: 'abc123',
      }),
    );

    expect(error.kind).toBe('dependency');
    expect(error.status).toBe(503);
    expect(error.message).toContain('Search');
    expect(error.service).toBe('Search');
    expect(error.correlationId).toBe('abc123');
    expect(error.retryable).toBe(true);
  });

  it('falls back to the correlation id header when the body has none', () => {
    const headers = new HttpHeaders({ 'X-Correlation-Id': 'from-header' });

    const error = toApiError(problemResponse(500, null, headers));

    expect(error.correlationId).toBe('from-header');
  });

  it('explains a request that never reached the server', () => {
    const error = toApiError(problemResponse(0, null));

    expect(error.kind).toBe('offline');
    expect(error.message).toContain('Cannot reach');
    expect(error.retryable).toBe(true);
  });

  it('treats an expired session as a sign-in problem regardless of the body', () => {
    const error = toApiError(
      problemResponse(401, { detail: 'Bearer token validation failed for issuer x.' }),
    );

    expect(error.kind).toBe('unauthorized');
    // The server's wording here is for logs, not for the reviewer.
    expect(error.message).toContain('Sign in again');
    expect(error.retryable).toBe(false);
  });

  it('does not offer a retry for a permission problem', () => {
    const error = toApiError(problemResponse(403, null));

    expect(error.kind).toBe('forbidden');
    expect(error.retryable).toBe(false);
  });

  it('keeps field errors from a validation problem', () => {
    const error = toApiError(
      problemResponse(400, {
        title: 'One or more validation errors occurred.',
        errors: {
          question: ['A question is required.'],
          scope: ['Scope must be one of: County, Case.'],
        },
      }),
    );

    expect(error.kind).toBe('validation');
    expect(error.fieldErrors['question']).toEqual(['A question is required.']);
    expect(error.fieldErrors['scope']).toHaveLength(1);
    expect(error.retryable).toBe(false);
  });

  it('ignores malformed field errors rather than rendering them', () => {
    const error = toApiError(
      problemResponse(400, { errors: { question: 'not an array', scope: [] } }),
    );

    expect(error.fieldErrors).toEqual({});
  });

  it.each([
    [502, 'dependency'],
    [503, 'dependency'],
    [504, 'dependency'],
    [500, 'server'],
    [404, 'notFound'],
    [409, 'conflict'],
    [422, 'validation'],
    [418, 'unknown'],
  ])('classifies status %i as %s', (status, kind) => {
    expect(toApiError(problemResponse(status, null)).kind).toBe(kind);
  });

  it('treats throttling as worth retrying', () => {
    expect(toApiError(problemResponse(429, null)).retryable).toBe(true);
  });

  it('survives a blob error body from the document viewer', () => {
    const error = toApiError(
      new HttpErrorResponse({
        status: 503,
        statusText: 'Service Unavailable',
        error: new Blob(['{}']),
      }),
    );

    expect(error.kind).toBe('dependency');
    expect(error.message).toContain('temporarily unavailable');
    expect(error.correlationId).toBeNull();
  });

  it('handles a failure that is not an HTTP response at all', () => {
    const error = toApiError(new Error('boom'));

    expect(error.kind).toBe('unknown');
    expect(error.message).toBe('Something went wrong. Try again.');
  });

  it('is idempotent, so a component can normalize an already-normalized error', () => {
    const first = toApiError(
      problemResponse(503, { detail: 'Search is down.', service: 'Search' }),
    );
    const second = toApiError(first);

    expect(second).toBe(first);
  });

  it('recognizes its own shape', () => {
    expect(isApiError(toApiError(problemResponse(500, null)))).toBe(true);
    expect(isApiError({ status: 500 })).toBe(false);
    expect(isApiError(null)).toBe(false);
  });
});
