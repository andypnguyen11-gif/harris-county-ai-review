import { Component, inject, input, output, signal } from '@angular/core';

import {
  CaseDocument,
  DEFAULT_DOCUMENT_TYPE,
  DOCUMENT_TYPES,
  DOCUMENT_TYPE_LABELS,
  DocumentType,
} from '../../core/models/document.model';
import { DocumentService } from '../../core/services/document.service';

export type UploadItemStatus = 'ready' | 'uploading' | 'success' | 'error';

export interface UploadItem {
  file: File;
  documentType: DocumentType;
  status: UploadItemStatus;
  progress: number;
  error: string | null;
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

  protected retry(item: UploadItem): void {
    this.upload(item);
  }

  protected upload(item: UploadItem): void {
    let current = this.patchItem(item, { status: 'uploading', progress: 0, error: null });

    this.documentService
      .uploadDocument(this.caseId(), current.file, current.documentType)
      .subscribe({
        next: (event) => {
          if (event.kind === 'progress') {
            current = this.patchItem(current, { progress: event.percent });
          } else {
            current = this.patchItem(current, { status: 'success', progress: 100 });
            this.uploaded.emit(event.document);
          }
        },
        error: () => {
          this.patchItem(current, {
            status: 'error',
            error: 'Upload failed. Check the file and try again.',
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
