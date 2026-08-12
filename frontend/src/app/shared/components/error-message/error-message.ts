import { Component, computed, input, output } from '@angular/core';

import { ApiError } from '../../../core/errors/api-error';

/**
 * The standard way this application tells a reviewer that something failed.
 *
 * Three things go on screen, and each earns its place. The message says what
 * happened in terms of the work being done. The retry button appears only when
 * retrying could actually help — offering it against a 403 would just teach
 * people to click it pointlessly. The reference id is shown last, small, and
 * selectable, because it is the one thing that lets support find the failure
 * in the logs; without it a reviewer's report is "it broke this morning".
 */
@Component({
  selector: 'app-error-message',
  templateUrl: './error-message.html',
  styleUrl: './error-message.scss',
  host: { class: 'error-message' },
})
export class ErrorMessage {
  readonly error = input.required<ApiError | null>();

  /**
   * Whether this element announces itself. Set false when the caller has
   * already wrapped it in its own alert region, so screen readers do not
   * announce two nested alerts for one failure.
   */
  readonly alert = input(true);

  /** Whether to offer a retry button for a failure worth retrying. */
  readonly showRetry = input(false);

  /** Emitted when the reviewer asks to try again. */
  readonly retry = output<void>();

  protected readonly canRetry = computed(
    () => this.showRetry() && (this.error()?.retryable ?? false),
  );

  protected readonly role = computed(() => (this.alert() ? 'alert' : null));

  protected onRetry(): void {
    this.retry.emit();
  }
}
