import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { Case } from '../../../core/models/case.model';
import { CaseService } from '../../../core/services/case.service';
import { StatusBadge } from '../../../shared/components/status-badge/status-badge';

@Component({
  selector: 'app-case-list',
  imports: [DatePipe, RouterLink, StatusBadge],
  templateUrl: './case-list.html',
  styleUrl: './case-list.scss',
})
export class CaseList implements OnInit {
  private readonly caseService = inject(CaseService);

  protected readonly cases = signal<Case[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal(false);

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
