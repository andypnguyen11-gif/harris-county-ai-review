import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ApiError } from '../../../core/errors/api-error';
import { ErrorMessage } from './error-message';

describe('ErrorMessage', () => {
  let fixture: ComponentFixture<ErrorMessage>;

  function makeError(overrides: Partial<ApiError> = {}): ApiError {
    return {
      kind: 'dependency',
      status: 503,
      message: 'The Search service is temporarily unavailable.',
      service: 'Search',
      correlationId: 'corr-42',
      fieldErrors: {},
      retryable: true,
      ...overrides,
    };
  }

  async function setup(
    error: ApiError | null,
    inputs: Record<string, unknown> = {},
  ): Promise<void> {
    TestBed.configureTestingModule({ imports: [ErrorMessage] });
    fixture = TestBed.createComponent(ErrorMessage);
    fixture.componentRef.setInput('error', error);
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    await fixture.whenStable();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  it('shows the message and the reference id', async () => {
    await setup(makeError());

    expect(el().textContent).toContain('The Search service is temporarily unavailable.');
    expect(el().textContent).toContain('corr-42');
  });

  it('announces itself as an alert by default', async () => {
    await setup(makeError());

    expect(el().querySelector('[role="alert"]')).not.toBeNull();
  });

  it('can stay silent when the caller already wraps it in an alert region', async () => {
    await setup(makeError(), { alert: false });

    expect(el().querySelector('[role="alert"]')).toBeNull();
    expect(el().textContent).toContain('temporarily unavailable');
  });

  it('renders nothing when there is no error', async () => {
    await setup(null);

    expect(el().textContent?.trim()).toBe('');
  });

  it('omits the reference id when the server did not send one', async () => {
    await setup(makeError({ correlationId: null }));

    expect(el().textContent).not.toContain('Reference id');
  });

  it('offers a retry only when retrying could help', async () => {
    await setup(makeError({ retryable: false }), { showRetry: true });

    expect(el().querySelector('button')).toBeNull();
  });

  it('does not offer a retry unless the caller asked for one', async () => {
    await setup(makeError());

    expect(el().querySelector('button')).toBeNull();
  });

  it('emits when the reviewer retries', async () => {
    await setup(makeError(), { showRetry: true });
    let retried = 0;
    fixture.componentInstance.retry.subscribe(() => (retried += 1));

    el().querySelector('button')!.click();
    await fixture.whenStable();

    expect(retried).toBe(1);
  });
});
