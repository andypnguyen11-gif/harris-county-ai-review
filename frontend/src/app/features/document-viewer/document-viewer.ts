import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, effect, inject, input, output, signal } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';

import { CitationTarget } from '../../core/models/citation-target.model';
import { CITATION_SOURCE_LABELS } from '../../core/models/question-answer.model';
import { DocumentService } from '../../core/services/document.service';

/** What the viewer is currently showing. */
export type ViewerState =
  | 'idle'
  | 'loading'
  /** A case document's file is loaded and rendered. */
  | 'rendered'
  /** A county document lives outside the app; the viewer links out to it. */
  | 'external'
  /** The document exists but its file could not be read. */
  | 'unavailable'
  /** Something else went wrong fetching the file. */
  | 'error';

/**
 * Shows the source behind a citation so a reviewer can verify an AI answer
 * against the document itself.
 *
 * The two corpora are opened differently because they are stored differently.
 * A case document is a file this system holds, so it is fetched and rendered
 * in place, opened at the cited page. A county document belongs to the
 * reference corpus and is published by the county, so the viewer shows its
 * metadata and links out to it rather than pretending to host it.
 *
 * Every failure is explained rather than left as a blank frame: a document
 * whose stored file has gone missing, a case citation with no case to fetch
 * it from, and a network failure each get their own message, and the source
 * metadata stays on screen throughout so the reviewer can still go find the
 * document by hand.
 */
@Component({
  selector: 'app-document-viewer',
  templateUrl: './document-viewer.html',
  styleUrl: './document-viewer.scss',
})
export class DocumentViewer implements OnDestroy {
  private readonly documentService = inject(DocumentService);
  private readonly sanitizer = inject(DomSanitizer);

  /** The source to show; null closes the viewer. */
  readonly target = input<CitationTarget | null>(null);

  /** Emitted when the reviewer dismisses the viewer. */
  readonly closed = output<void>();

  protected readonly state = signal<ViewerState>('idle');
  protected readonly fileUrl = signal<SafeResourceUrl | null>(null);
  protected readonly sourceLabels = CITATION_SOURCE_LABELS;

  /** The object URL currently held, so it can be released when it is replaced. */
  private objectUrl: string | null = null;

  constructor() {
    effect(() => {
      const target = this.target();
      this.release();

      if (target === null) {
        this.state.set('idle');
        return;
      }

      if (target.source === 'County') {
        // County documents are published by the county, not stored here.
        this.state.set('external');
        return;
      }

      if (!target.caseId) {
        // A case document can only be fetched through its case.
        this.state.set('unavailable');
        return;
      }

      this.load(target.caseId, target.documentId, target.page);
    });
  }

  ngOnDestroy(): void {
    this.release();
  }

  /** The county document's URL, pointed at the cited page when it names one. */
  protected externalUrl(): string | null {
    const target = this.target();
    if (!target?.sourceUrl) {
      return null;
    }

    return target.page === null ? target.sourceUrl : `${target.sourceUrl}#page=${target.page}`;
  }

  protected retry(): void {
    const target = this.target();
    if (target?.source === 'Case' && target.caseId) {
      this.load(target.caseId, target.documentId, target.page);
    }
  }

  protected close(): void {
    this.closed.emit();
  }

  private load(caseId: string, documentId: string, page: number | null): void {
    this.state.set('loading');
    this.documentService.getDocumentContent(caseId, documentId).subscribe({
      next: (blob) => {
        this.release();
        this.objectUrl = URL.createObjectURL(blob);
        // The page fragment is how a browser's built-in PDF viewer is told
        // where to open; a viewer that ignores it simply opens at page one.
        const url = page === null ? this.objectUrl : `${this.objectUrl}#page=${page}`;
        this.fileUrl.set(this.sanitizer.bypassSecurityTrustResourceUrl(url));
        this.state.set('rendered');
      },
      error: (error: HttpErrorResponse) => {
        // 404 covers both "no such document" and "its file is gone"; either
        // way there is nothing for the reviewer to look at here.
        this.state.set(error.status === 404 ? 'unavailable' : 'error');
      },
    });
  }

  /** Releases the object URL so a long review session does not leak blobs. */
  private release(): void {
    if (this.objectUrl !== null) {
      URL.revokeObjectURL(this.objectUrl);
      this.objectUrl = null;
    }

    this.fileUrl.set(null);
  }
}
