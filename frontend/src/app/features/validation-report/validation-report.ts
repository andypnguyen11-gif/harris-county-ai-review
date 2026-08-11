import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, input, signal } from '@angular/core';

import { DOCUMENT_TYPE_LABELS } from '../../core/models/document.model';
import {
  VALIDATION_TYPE_LABELS,
  ValidationReport,
  ValidationReportItem,
} from '../../core/models/validation.model';
import { ValidationService } from '../../core/services/validation.service';
import { StatusBadge } from '../../shared/components/status-badge/status-badge';

/** Rule results that concern the same document, in workflow rule order. */
export interface ValidationGroup {
  label: string;
  items: ValidationReportItem[];
}

/** Label for results that concern the submission as a whole rather than one document. */
const PACKAGE_GROUP_LABEL = 'Submission package';

/**
 * Shows the latest validation report for a case and lets the reviewer run
 * (or re-run) validation. Results are grouped by the document the evidence
 * came from; document-level checks are grouped under the submission package.
 */
@Component({
  selector: 'app-validation-report',
  imports: [DatePipe, StatusBadge],
  templateUrl: './validation-report.html',
  styleUrl: './validation-report.scss',
})
export class ValidationReportPanel implements OnInit {
  private readonly validationService = inject(ValidationService);

  readonly caseId = input.required<string>();

  protected readonly report = signal<ValidationReport | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);
  protected readonly running = signal(false);
  protected readonly runError = signal(false);

  protected readonly validationTypeLabels = VALIDATION_TYPE_LABELS;

  protected readonly groups = computed<ValidationGroup[]>(() => {
    const report = this.report();
    if (!report) {
      return [];
    }

    const groups = new Map<string, ValidationReportItem[]>();
    for (const item of report.items) {
      const label = item.documentType
        ? DOCUMENT_TYPE_LABELS[item.documentType]
        : PACKAGE_GROUP_LABEL;
      const items = groups.get(label);
      if (items) {
        items.push(item);
      } else {
        groups.set(label, [item]);
      }
    }

    return [...groups.entries()].map(([label, items]) => ({ label, items }));
  });

  protected readonly summary = computed(() => {
    const items = this.report()?.items ?? [];
    return {
      total: items.length,
      complete: items.filter((item) => item.status === 'Complete').length,
      issues: items.filter(
        (item) =>
          item.status === 'Missing' ||
          item.status === 'Invalid' ||
          item.status === 'PotentiallyIncomplete',
      ).length,
      review: items.filter(
        (item) => item.status === 'NeedsHumanReview' || item.status === 'UnableToDetermine',
      ).length,
    };
  });

  ngOnInit(): void {
    this.loadLatest();
  }

  protected loadLatest(): void {
    this.loading.set(true);
    this.loadError.set(false);
    this.validationService.getLatestReport(this.caseId()).subscribe({
      next: (report) => {
        this.report.set(report);
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        // 404 means the case simply has not been validated yet.
        if (error.status === 404) {
          this.report.set(null);
        } else {
          this.loadError.set(true);
        }
        this.loading.set(false);
      },
    });
  }

  protected run(): void {
    this.running.set(true);
    this.runError.set(false);
    this.validationService.runValidation(this.caseId()).subscribe({
      next: (report) => {
        this.report.set(report);
        this.loadError.set(false);
        this.running.set(false);
      },
      error: () => {
        this.runError.set(true);
        this.running.set(false);
      },
    });
  }
}
