import { HttpErrorResponse } from '@angular/common/http';

/**
 * What kind of thing went wrong, from the point of view of what the reviewer
 * should do next.
 *
 * This is deliberately coarser than an HTTP status. A reviewer does not act
 * differently on a 502 than on a 503 — in both cases something the feature
 * depends on is down and waiting is the right move — so both are `dependency`.
 * What they do act differently on is "your session ended", "the file is
 * wrong", and "the service is down", so those stay apart.
 */
export type ApiErrorKind =
  | 'offline'
  | 'unauthorized'
  | 'forbidden'
  | 'notFound'
  | 'validation'
  | 'conflict'
  | 'dependency'
  | 'server'
  | 'unknown';

/** A failed API call, normalized into something a component can render. */
export interface ApiError {
  readonly kind: ApiErrorKind;

  /** HTTP status, or 0 when the request never reached the server. */
  readonly status: number;

  /** One sentence to show the reviewer. Always populated. */
  readonly message: string;

  /** The capability that failed ("Search"), when the API named one. */
  readonly service: string | null;

  /** The id that ties this failure to the server logs, when the API sent one. */
  readonly correlationId: string | null;

  /** Field-level messages from a validation problem, keyed by field name. */
  readonly fieldErrors: Readonly<Record<string, readonly string[]>>;

  /** Whether trying the same thing again has a reasonable chance of working. */
  readonly retryable: boolean;
}

const CORRELATION_ID_HEADER = 'X-Correlation-Id';

/**
 * RFC 9457 problem document, as this API sends it. Only the fields the UI
 * reads are declared; anything else is ignored.
 */
interface ProblemDetails {
  readonly title?: unknown;
  readonly detail?: unknown;
  readonly status?: unknown;
  readonly correlationId?: unknown;
  readonly service?: unknown;
  readonly errors?: unknown;
}

/**
 * Normalizes anything thrown by an HTTP call into an {@link ApiError}.
 *
 * The server's `detail` is preferred when there is one, because the API writes
 * it for exactly this purpose and it names the failed capability. The
 * fallbacks below are what the reviewer sees when the server never got to
 * write anything — which is precisely the case where a generic "an error
 * occurred" would be least useful.
 */
export function toApiError(error: unknown): ApiError {
  if (isApiError(error)) {
    return error;
  }

  if (!(error instanceof HttpErrorResponse)) {
    return {
      kind: 'unknown',
      status: 0,
      message: 'Something went wrong. Try again.',
      service: null,
      correlationId: null,
      fieldErrors: {},
      retryable: true,
    };
  }

  const problem = readProblemDetails(error);
  const kind = classify(error.status);
  const service = readString(problem?.service);
  const correlationId =
    readString(problem?.correlationId) ?? error.headers?.get(CORRELATION_ID_HEADER) ?? null;

  return {
    kind,
    status: error.status,
    message: buildMessage(kind, problem, service),
    service,
    correlationId,
    fieldErrors: readFieldErrors(problem?.errors),
    retryable:
      kind === 'offline' || kind === 'dependency' || kind === 'server' || error.status === 429,
  };
}

/** Type guard for a value already normalized by {@link toApiError}. */
export function isApiError(value: unknown): value is ApiError {
  return (
    typeof value === 'object' &&
    value !== null &&
    'kind' in value &&
    'status' in value &&
    'message' in value &&
    'retryable' in value
  );
}

function classify(status: number): ApiErrorKind {
  // Angular reports a request that never reached the server as status 0.
  if (status === 0) {
    return 'offline';
  }
  if (status === 401) {
    return 'unauthorized';
  }
  if (status === 403) {
    return 'forbidden';
  }
  if (status === 404) {
    return 'notFound';
  }
  if (status === 409) {
    return 'conflict';
  }
  if (status === 400 || status === 422) {
    return 'validation';
  }
  // 502, 503, and 504 all mean "something behind the API is unhealthy".
  if (status === 502 || status === 503 || status === 504) {
    return 'dependency';
  }
  if (status >= 500) {
    return 'server';
  }
  return 'unknown';
}

function buildMessage(
  kind: ApiErrorKind,
  problem: ProblemDetails | null,
  service: string | null,
): string {
  const detail = readString(problem?.detail);

  // A 401 is about the session, not about whatever the server said, so its
  // message is never taken from the body.
  if (kind !== 'unauthorized' && detail !== null) {
    return detail;
  }

  switch (kind) {
    case 'offline':
      return 'Cannot reach the review service. Check your connection and try again.';
    case 'unauthorized':
      return 'Your session has ended. Sign in again to continue.';
    case 'forbidden':
      return 'You do not have permission to do that.';
    case 'notFound':
      return 'That item could not be found.';
    case 'conflict':
      return 'That change conflicts with the current state of the record. Reload and try again.';
    case 'validation':
      return (
        readString(problem?.title) ?? 'The request was rejected. Check the highlighted fields.'
      );
    case 'dependency':
      return service === null
        ? 'A service this feature depends on is temporarily unavailable. Try again in a moment.'
        : `The ${service} service is temporarily unavailable, so this feature is unavailable right now.`;
    case 'server':
      return 'Something went wrong on our side. Quote the reference id below if you report this.';
    default:
      return 'Something went wrong. Try again.';
  }
}

function readProblemDetails(error: HttpErrorResponse): ProblemDetails | null {
  // A blob-typed request (the document viewer) gets a Blob body even for the
  // error, so there is nothing to read synchronously. The status-based
  // fallbacks cover it.
  return typeof error.error === 'object' && error.error !== null && !(error.error instanceof Blob)
    ? (error.error as ProblemDetails)
    : null;
}

function readString(value: unknown): string | null {
  return typeof value === 'string' && value.trim().length > 0 ? value : null;
}

function readFieldErrors(value: unknown): Readonly<Record<string, readonly string[]>> {
  if (typeof value !== 'object' || value === null) {
    return {};
  }

  const result: Record<string, readonly string[]> = {};
  for (const [field, messages] of Object.entries(value)) {
    if (Array.isArray(messages)) {
      const strings = messages.filter((message): message is string => typeof message === 'string');
      if (strings.length > 0) {
        result[field] = strings;
      }
    }
  }

  return result;
}
