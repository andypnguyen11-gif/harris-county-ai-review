import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { ApiError, toApiError } from '../../../core/errors/api-error';
import { Case } from '../../../core/models/case.model';
import { CaseService } from '../../../core/services/case.service';
import { ErrorMessage } from '../../../shared/components/error-message/error-message';
import { StatusBadge } from '../../../shared/components/status-badge/status-badge';

@Component({
  selector: 'app-case-list',
  imports: [DatePipe, RouterLink, ErrorMessage, StatusBadge],
  templateUrl: './case-list.html',
  styleUrl: './case-list.scss',
})
export class CaseList implements OnInit {
  private readonly caseService = inject(CaseService);

  protected readonly cases = signal<Case[]>([]);
  protected readonly loading = signal(true);
  /** The failure that stopped the list loading, or null when there was none. */
  protected readonly error = signal<ApiError | null>(null);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.caseService.getCases().subscribe({
      next: (cases) => {
        this.cases.set(cases);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(toApiError(failure));
        this.loading.set(false);
      },
    });
  }
}
