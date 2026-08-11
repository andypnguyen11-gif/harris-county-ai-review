import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { CASE_STATUSES, CASE_STATUS_LABELS, Case, CaseStatus } from '../../core/models/case.model';
import { CaseService } from '../../core/services/case.service';
import { StatusBadge } from '../../shared/components/status-badge/status-badge';

interface StatusSummary {
  status: CaseStatus;
  label: string;
  count: number;
}

@Component({
  selector: 'app-dashboard',
  imports: [DatePipe, RouterLink, StatusBadge],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard implements OnInit {
  private readonly caseService = inject(CaseService);

  protected readonly cases = signal<Case[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal(false);

  protected readonly totalCases = computed(() => this.cases().length);

  protected readonly statusSummaries = computed<StatusSummary[]>(() => {
    const cases = this.cases();
    return CASE_STATUSES.map((status) => ({
      status,
      label: CASE_STATUS_LABELS[status],
      count: cases.filter((c) => c.status === status).length,
    }));
  });

  protected readonly recentCases = computed(() =>
    [...this.cases()]
      .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
      .slice(0, 5),
  );

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.caseService.getCases().subscribe({
      next: (cases) => {
        this.cases.set(cases);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }
}
