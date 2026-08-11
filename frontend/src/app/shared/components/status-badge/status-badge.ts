import { Component, computed, input } from '@angular/core';

import { CASE_STATUS_LABELS, CaseStatus } from '../../../core/models/case.model';
import {
  DOCUMENT_PROCESSING_STATUS_LABELS,
  DocumentProcessingStatus,
} from '../../../core/models/document.model';

export type BadgeStatus = CaseStatus | DocumentProcessingStatus;

const BADGE_LABELS: Record<BadgeStatus, string> = {
  ...CASE_STATUS_LABELS,
  ...DOCUMENT_PROCESSING_STATUS_LABELS,
};

@Component({
  selector: 'app-status-badge',
  template: `<span class="status-badge" [class]="badgeClass()">{{ label() }}</span>`,
  styleUrl: './status-badge.scss',
})
export class StatusBadge {
  readonly status = input.required<BadgeStatus>();

  protected readonly label = computed(() => BADGE_LABELS[this.status()]);
  protected readonly badgeClass = computed(
    () => `status-badge status-badge--${this.status().toLowerCase()}`,
  );
}
