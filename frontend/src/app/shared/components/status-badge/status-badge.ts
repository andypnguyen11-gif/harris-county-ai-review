import { Component, computed, input } from '@angular/core';

import { CASE_STATUS_LABELS, CaseStatus } from '../../../core/models/case.model';

@Component({
  selector: 'app-status-badge',
  template: `<span class="status-badge" [class]="badgeClass()">{{ label() }}</span>`,
  styleUrl: './status-badge.scss',
})
export class StatusBadge {
  readonly status = input.required<CaseStatus>();

  protected readonly label = computed(() => CASE_STATUS_LABELS[this.status()]);
  protected readonly badgeClass = computed(
    () => `status-badge status-badge--${this.status().toLowerCase()}`,
  );
}
