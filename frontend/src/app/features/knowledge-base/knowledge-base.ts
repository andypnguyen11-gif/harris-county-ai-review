import { DatePipe } from '@angular/common';
import { Component, ElementRef, OnInit, inject, signal, viewChild } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import {
  KNOWLEDGE_DOCUMENT_INGESTION_STATUS_LABELS,
  KnowledgeDocument,
} from '../../core/models/knowledge-document.model';
import { KnowledgeBaseService } from '../../core/services/knowledge-base.service';

@Component({
  selector: 'app-knowledge-base',
  imports: [DatePipe, ReactiveFormsModule],
  templateUrl: './knowledge-base.html',
  styleUrl: './knowledge-base.scss',
})
export class KnowledgeBase implements OnInit {
  private readonly formBuilder = inject(NonNullableFormBuilder);
  private readonly knowledgeBaseService = inject(KnowledgeBaseService);

  private readonly fileInput = viewChild.required<ElementRef<HTMLInputElement>>('fileInput');

  protected readonly ingestionStatusLabels = KNOWLEDGE_DOCUMENT_INGESTION_STATUS_LABELS;

  protected readonly documents = signal<KnowledgeDocument[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal(false);
  protected readonly includeDeactivated = signal(false);

  protected readonly selectedFile = signal<File | null>(null);
  protected readonly fileMissing = signal(false);
  protected readonly submitting = signal(false);
  protected readonly submitError = signal(false);
  protected readonly deactivatingId = signal<string | null>(null);
  protected readonly deactivateError = signal(false);

  protected readonly form = this.formBuilder.group({
    title: ['', [Validators.required, Validators.maxLength(300)]],
    department: ['', [Validators.required, Validators.maxLength(200)]],
    permitType: ['', [Validators.required, Validators.maxLength(100)]],
    documentType: ['', [Validators.required, Validators.maxLength(100)]],
    version: [''],
    effectiveDate: [''],
    sourceUrl: [''],
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.knowledgeBaseService.getDocuments(this.includeDeactivated()).subscribe({
      next: (documents) => {
        this.documents.set(documents);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  protected onIncludeDeactivatedChange(event: Event): void {
    this.includeDeactivated.set((event.target as HTMLInputElement).checked);
    this.load();
  }

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = input.files;
    this.selectedFile.set(files && files.length > 0 ? files[0] : null);
    this.fileMissing.set(false);
  }

  protected showFieldError(name: 'title' | 'department' | 'permitType' | 'documentType'): boolean {
    const control = this.form.controls[name];
    return control.invalid && (control.touched || control.dirty);
  }

  protected onSubmit(): void {
    this.submitError.set(false);
    const file = this.selectedFile();
    this.fileMissing.set(file === null);

    if (this.form.invalid || file === null) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    const { title, department, permitType, documentType, version, effectiveDate, sourceUrl } =
      this.form.getRawValue();

    this.knowledgeBaseService
      .uploadDocument({
        file,
        title: title.trim(),
        department: department.trim(),
        permitType: permitType.trim(),
        documentType: documentType.trim(),
        version: version.trim() || undefined,
        effectiveDate: effectiveDate || undefined,
        sourceUrl: sourceUrl.trim() || undefined,
      })
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.resetForm();
          this.load();
        },
        error: () => {
          this.submitting.set(false);
          this.submitError.set(true);
        },
      });
  }

  protected deactivate(document: KnowledgeDocument): void {
    this.deactivateError.set(false);
    this.deactivatingId.set(document.id);
    this.knowledgeBaseService.deactivateDocument(document.id).subscribe({
      next: () => {
        this.deactivatingId.set(null);
        this.load();
      },
      error: () => {
        this.deactivatingId.set(null);
        this.deactivateError.set(true);
      },
    });
  }

  protected isDeactivated(document: KnowledgeDocument): boolean {
    return document.ingestionStatus === 'Deactivated';
  }

  private resetForm(): void {
    this.form.reset();
    this.selectedFile.set(null);
    this.fileMissing.set(false);
    this.fileInput().nativeElement.value = '';
  }
}
