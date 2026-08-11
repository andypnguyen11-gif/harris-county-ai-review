import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { WORKFLOW_TYPE_LABELS, Case } from '../../../core/models/case.model';
import { CaseDocument, DOCUMENT_TYPE_LABELS } from '../../../core/models/document.model';
import { CaseService } from '../../../core/services/case.service';
import { DocumentService } from '../../../core/services/document.service';
import { StatusBadge } from '../../../shared/components/status-badge/status-badge';
import { DocumentUpload } from '../../document-upload/document-upload';

@Component({
  selector: 'app-case-detail',
  imports: [DatePipe, RouterLink, StatusBadge, DocumentUpload],
  templateUrl: './case-detail.html',
  styleUrl: './case-detail.scss',
})
export class CaseDetail implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly caseService = inject(CaseService);
  private readonly documentService = inject(DocumentService);

  protected readonly caseItem = signal<Case | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal(false);

  protected readonly documents = signal<CaseDocument[]>([]);
  protected readonly documentsLoading = signal(true);
  protected readonly documentsError = signal(false);

  protected readonly workflowTypeLabels = WORKFLOW_TYPE_LABELS;
  protected readonly documentTypeLabels = DOCUMENT_TYPE_LABELS;

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
        this.loadDocuments();
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  protected loadDocuments(): void {
    const caseItem = this.caseItem();
    if (!caseItem) {
      return;
    }

    this.documentsLoading.set(true);
    this.documentsError.set(false);
    this.documentService.getDocuments(caseItem.id).subscribe({
      next: (documents) => {
        this.documents.set(documents);
        this.documentsLoading.set(false);
      },
      error: () => {
        this.documentsError.set(true);
        this.documentsLoading.set(false);
      },
    });
  }

  protected onDocumentUploaded(document: CaseDocument): void {
    this.documents.update((documents) => [...documents, document]);
  }
}
