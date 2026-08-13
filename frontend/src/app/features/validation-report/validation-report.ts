import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, input, signal } from '@angular/core';

import { CitationTarget } from '../../core/models/citation-target.model';
import { DOCUMENT_TYPE_LABELS } from '../../core/models/document.model';
import {
  VALIDATION_TYPE_LABELS,
  ValidationReport,
  ValidationReportItem,
  ValidationStatus,
} from '../../core/models/validation.model';
import { ValidationService } from '../../core/services/validation.service';
import { StatusBadge } from '../../shared/components/status-badge/status-badge';
import { DocumentViewer, ViewerRegion } from '../document-viewer/document-viewer';

/** Rule results that concern the same document, in workflow rule order. */
export interface ValidationGroup {
  label: string;
  items: ValidationReportItem[];
}

/** Label for results that concern the submission as a whole rather than one document. */
const PACKAGE_GROUP_LABEL = 'Submission package';

/**
 * The statuses worth boxing. A reviewer opens the page to find what went
 * wrong, so satisfied fields stay unboxed even when their region is known —
 * boxing everything on a dense permit form is noise.
 */
const ISSUE_STATUSES: readonly ValidationStatus[] = ['Missing', 'Invalid', 'PotentiallyIncomplete'];

/**
 * Shows the latest validation report for a case and lets the reviewer run
 * (or re-run) validation. Results are grouped by the document the evidence
 * came from; document-level checks are grouped under the submission package.
 */
@Component({
  selector: 'app-validation-report',
  imports: [DatePipe, StatusBadge, DocumentViewer],
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
  /** The evidence document the viewer is showing, or null when it is closed. */
  protected readonly viewerTarget = signal<CitationTarget | null>(null);
  /** The finding whose box is drawn active, or null when none is. */
  protected readonly activeItemId = signal<string | null>(null);

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

  /**
   * The boxes the viewer draws for the open document and page, per the draw
   * policy in docs/architecture/document-region-highlighting.md. The viewer
   * draws what it is given; the policy lives here.
   *
   * Public so the policy can be asserted directly in tests.
   */
  readonly viewerRegions = computed<ViewerRegion[]>(() => {
    const target = this.viewerTarget();
    if (target === null || target.page === null) {
      return [];
    }

    const activeId = this.activeItemId();
    return (this.report()?.items ?? [])
      .filter(
        (item) =>
          item.documentId === target.documentId &&
          item.boundingBox !== null &&
          item.boundingBox.pageNumber === target.page &&
          ISSUE_STATUSES.includes(item.status),
      )
      .map((item) => ({
        id: item.id,
        box: item.boundingBox!,
        active: item.id === activeId,
        label: `${item.requirement}: ${item.message}`,
      }));
  });

  /** Why the open page carries no box for the finding being viewed. */
  readonly viewerNotice = computed<string | null>(() => {
    const activeId = this.activeItemId();
    if (this.viewerTarget() === null || activeId === null) {
      return null;
    }

    const item = (this.report()?.items ?? []).find((candidate) => candidate.id === activeId);
    return item !== undefined && item.boundingBox === null
      ? "Couldn't locate this field on the page."
      : null;
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

  /**
   * Opens the submitted document a finding was based on, at the page it was
   * found on. Always a case document — validation reports are about what this
   * applicant submitted, never about the county corpus.
   */
  protected openEvidence(item: ValidationReportItem): void {
    if (!item.documentId) {
      return;
    }

    // A second click on the finding already showing clears its highlight and
    // leaves the page's other boxes in place.
    this.activeItemId.update((current) => (current === item.id ? null : item.id));

    this.viewerTarget.set({
      source: 'Case',
      caseId: this.caseId(),
      documentId: item.documentId,
      title: item.documentType ? DOCUMENT_TYPE_LABELS[item.documentType] : item.requirement,
      section: item.requirement,
      page: item.pageNumber,
      sourceUrl: null,
    });
  }

  protected closeEvidence(): void {
    this.viewerTarget.set(null);
    this.activeItemId.set(null);
  }
}
