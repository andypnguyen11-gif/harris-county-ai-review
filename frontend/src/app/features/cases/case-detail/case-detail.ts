import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { WORKFLOW_TYPE_LABELS, Case } from '../../../core/models/case.model';
import { CaseService } from '../../../core/services/case.service';
import { StatusBadge } from '../../../shared/components/status-badge/status-badge';

@Component({
  selector: 'app-case-detail',
  imports: [DatePipe, RouterLink, StatusBadge],
  templateUrl: './case-detail.html',
  styleUrl: './case-detail.scss',
})
export class CaseDetail implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly caseService = inject(CaseService);

  protected readonly caseItem = signal<Case | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal(false);

  protected readonly workflowTypeLabels = WORKFLOW_TYPE_LABELS;

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.loading.set(false);
      this.error.set(true);
      return;
    }

    this.loading.set(true);
    this.error.set(false);
    this.caseService.getCase(id).subscribe({
      next: (caseItem) => {
        this.caseItem.set(caseItem);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }
}
