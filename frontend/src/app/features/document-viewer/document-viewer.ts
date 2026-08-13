import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  ElementRef,
  OnDestroy,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';

import { CitationTarget } from '../../core/models/citation-target.model';
import { CITATION_SOURCE_LABELS } from '../../core/models/question-answer.model';
import { DocumentService } from '../../core/services/document.service';
import { PdfDocumentHandle, PdfRenderService } from '../../core/services/pdf-render.service';

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
  private readonly pdf = inject(PdfRenderService);

  /** The source to show; null closes the viewer. */
  readonly target = input<CitationTarget | null>(null);

  /** Emitted when the reviewer dismisses the viewer. */
  readonly closed = output<void>();

  protected readonly state = signal<ViewerState>('idle');
  protected readonly sourceLabels = CITATION_SOURCE_LABELS;

  /** The canvas only exists while the state is 'rendered'. */
  private readonly canvas = viewChild<ElementRef<HTMLCanvasElement>>('pdfCanvas');
  private readonly handle = signal<PdfDocumentHandle | null>(null);

  /** The page being shown; a citation without a page opens at the first. */
  protected readonly pageNumber = signal(1);

  constructor() {
    effect(() => {
      const target = this.target();
      this.pageNumber.set(target?.page ?? 1);

      if (target === null) {
        this.release();
        this.state.set('idle');
        return;
      }

      if (target.source === 'County') {
        // County documents are published by the county, not stored here.
        this.release();
        this.state.set('external');
        return;
      }

      if (!target.caseId) {
        // A case document can only be fetched through its case.
        this.release();
        this.state.set('unavailable');
        return;
      }

      // Turning to another page of the document already open is not a reload.
      if (this.loadedDocumentId === target.documentId && this.handle() !== null) {
        return;
      }

      this.release();
      this.load(target.caseId, target.documentId);
    });

    // Rendering has to wait for the canvas, which the template only creates
    // once the state is 'rendered'. Depending on the view query rather than
    // calling render inline is what sequences the two.
    effect(() => {
      const canvas = this.canvas()?.nativeElement;
      const handle = this.handle();
      const page = this.pageNumber();
      if (!canvas || !handle) {
        return;
      }

      void this.draw(handle, page, canvas);
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
      this.release();
      this.load(target.caseId, target.documentId);
    }
  }

  protected close(): void {
    this.closed.emit();
  }

  private loadedDocumentId: string | null = null;

  private load(caseId: string, documentId: string): void {
    this.state.set('loading');
    this.documentService.getDocumentContent(caseId, documentId).subscribe({
      next: async (blob) => {
        try {
          const handle = await this.pdf.open(blob);
          this.loadedDocumentId = documentId;
          this.handle.set(handle);
          this.state.set('rendered');
        } catch {
          // The file arrived but pdf.js could not parse it.
          this.state.set('error');
        }
      },
      error: (error: HttpErrorResponse) => {
        // 404 covers both "no such document" and "its file is gone"; either
        // way there is nothing for the reviewer to look at here.
        this.state.set(error.status === 404 ? 'unavailable' : 'error');
      },
    });
  }

  private async draw(
    handle: PdfDocumentHandle,
    page: number,
    canvas: HTMLCanvasElement,
  ): Promise<void> {
    const width = canvas.parentElement?.clientWidth || canvas.clientWidth || 800;
    try {
      await handle.renderPage(page, canvas, width);
    } catch {
      this.state.set('error');
    }
  }

  /** Closes the open document so a long review session does not leak memory. */
  private release(): void {
    this.handle()?.destroy();
    this.handle.set(null);
    this.loadedDocumentId = null;
  }
}
