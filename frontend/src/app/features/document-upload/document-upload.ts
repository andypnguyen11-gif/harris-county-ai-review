import { Component, inject, input, output, signal } from '@angular/core';

import { toApiError } from '../../core/errors/api-error';
import {
  CaseDocument,
  DEFAULT_DOCUMENT_TYPE,
  DOCUMENT_TYPES,
  DOCUMENT_TYPE_LABELS,
  DocumentType,
} from '../../core/models/document.model';
import { DocumentService } from '../../core/services/document.service';

export type UploadItemStatus = 'ready' | 'uploading' | 'processing' | 'success' | 'error';

export interface UploadItem {
  file: File;
  documentType: DocumentType;
  status: UploadItemStatus;
  progress: number;
  error: string | null;
  /**
   * Set once the API has stored the file. Its presence is what makes a retry
   * repeat only the processing step instead of re-uploading — a document that
   * failed extraction is already stored, and uploading it again would leave
   * the case with a duplicate.
   */
  documentId: string | null;
}

@Component({
  selector: 'app-document-upload',
  templateUrl: './document-upload.html',
  styleUrl: './document-upload.scss',
})
export class DocumentUpload {
  private readonly documentService = inject(DocumentService);

  readonly caseId = input.required<string>();

  /** Emits each document the API stores successfully. */
  readonly uploaded = output<CaseDocument>();

  /**
   * Emits each document after its processing run finishes, whether it came
   * back Normalized or Failed, so the case can show the outcome rather than a
   * stale Uploaded status.
   */
  readonly processed = output<CaseDocument>();

  protected readonly items = signal<UploadItem[]>([]);
  protected readonly dragActive = signal(false);
  protected readonly rejectedFiles = signal<string[]>([]);

  protected readonly documentTypes = DOCUMENT_TYPES;
  protected readonly documentTypeLabels = DOCUMENT_TYPE_LABELS;

  protected onFileInputChange(event: Event): void {
    const inputElement = event.target as HTMLInputElement;
    this.addFiles(Array.from(inputElement.files ?? []));
    inputElement.value = '';
  }

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragActive.set(true);
  }

  protected onDragLeave(): void {
    this.dragActive.set(false);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragActive.set(false);
    this.addFiles(Array.from(event.dataTransfer?.files ?? []));
  }

  protected addFiles(files: File[]): void {
    const accepted = files.filter((file) => this.isPdf(file));
    const rejected = files.filter((file) => !this.isPdf(file)).map((file) => file.name);

    this.rejectedFiles.set(rejected);

    if (accepted.length === 0) {
      return;
    }

    const newItems: UploadItem[] = accepted.map((file) => ({
      file,
      documentType: DEFAULT_DOCUMENT_TYPE,
      status: 'ready',
      progress: 0,
      error: null,
      documentId: null,
    }));

    this.items.update((items) => [...items, ...newItems]);
  }

  protected setDocumentType(item: UploadItem, event: Event): void {
    const value = (event.target as HTMLSelectElement).value as DocumentType;
    this.patchItem(item, { documentType: value });
  }

  protected removeItem(item: UploadItem): void {
    this.items.update((items) => items.filter((candidate) => candidate !== item));
  }

  protected uploadAll(): void {
    for (const item of this.items()) {
      if (item.status === 'ready') {
        this.upload(item);
      }
    }
  }

  /**
   * Resumes a failed row at the step that failed: a file that never reached
   * the API is uploaded again, while one that is already stored only reruns
   * processing.
   */
  protected retry(item: UploadItem): void {
    if (item.documentId) {
      this.process(item, item.documentId);
    } else {
      this.upload(item);
    }
  }

  protected upload(item: UploadItem): void {
    let current = this.patchItem(item, { status: 'uploading', progress: 0, error: null });

    this.documentService
      .uploadDocument(this.caseId(), current.file, current.documentType)
      .subscribe({
        next: (event) => {
          if (event.kind === 'progress') {
            current = this.patchItem(current, { progress: event.percent });
            return;
          }

          current = this.patchItem(current, { progress: 100, documentId: event.document.id });
          this.uploaded.emit(event.document);

          // The file is stored; extraction is a separate request so an
          // extraction outage never costs the reviewer the upload.
          this.process(current, event.document.id);
        },
        error: (failure: unknown) => {
          // The API explains blob-storage and validation failures precisely;
          // the generic sentence is only for failures it could not describe.
          const apiError = toApiError(failure);
          this.patchItem(current, {
            status: 'error',
            error:
              apiError.kind === 'unknown'
                ? 'Upload failed. Check the file and try again.'
                : `Upload failed. ${apiError.message}`,
          });
        },
      });
  }

  /**
   * Runs the stored document through the extraction pipeline. A run that fails
   * comes back as a normal response carrying the document's Failed status and
   * a reason, so the row shows what went wrong instead of reporting success.
   */
  private process(item: UploadItem, documentId: string): void {
    const current = this.patchItem(item, { status: 'processing', error: null });

    this.documentService.processDocument(this.caseId(), documentId).subscribe({
      next: (result) => {
        this.processed.emit(result.document);

        if (result.document.processingStatus === 'Failed') {
          this.patchItem(current, {
            status: 'error',
            error: result.failureReason
              ? `Processing failed: ${result.failureReason}`
              : 'Processing failed. Retry, or replace the file and upload it again.',
          });
          return;
        }

        this.patchItem(current, { status: 'success' });
      },
      error: () => {
        // The upload itself is safe; only this step needs retrying.
        this.patchItem(current, {
          status: 'error',
          error: 'The file was uploaded, but processing could not be started. Try again.',
        });
      },
    });
  }

  protected hasReadyItems(): boolean {
    return this.items().some((item) => item.status === 'ready');
  }

  protected formatFileSize(bytes: number): string {
    if (bytes < 1024) {
      return `${bytes} B`;
    }

    if (bytes < 1024 * 1024) {
      return `${(bytes / 1024).toFixed(1)} KB`;
    }

    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  private isPdf(file: File): boolean {
    return file.type === 'application/pdf' || file.name.toLowerCase().endsWith('.pdf');
  }

  /** Immutably replaces an item, returning the replacement. */
  private patchItem(item: UploadItem, patch: Partial<UploadItem>): UploadItem {
    const updated: UploadItem = { ...item, ...patch };
    this.items.update((items) =>
      items.map((candidate) => (candidate === item ? updated : candidate)),
    );
    return updated;
  }
}
