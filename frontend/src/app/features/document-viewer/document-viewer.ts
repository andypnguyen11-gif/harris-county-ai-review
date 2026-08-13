import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  ElementRef,
  OnDestroy,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';

import { CitationTarget } from '../../core/models/citation-target.model';
import { CITATION_SOURCE_LABELS } from '../../core/models/question-answer.model';
import { BoundingBox } from '../../core/models/validation.model';
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
 * Zoom steps, as multiples of the width that fits the viewer. A reviewer
 * reading small print on a scanned form needs more than the column can show,
 * so the page is allowed to outgrow it and scroll.
 */
const ZOOM_LEVELS = [1, 1.25, 1.5, 2, 2.5, 3];

/**
 * How much room the viewer must gain before the page is redrawn to fill it.
 * Roughly a scrollbar: a page that has just lost one must not grow back into
 * the space it freed, or a page whose height straddles the viewer's own
 * summons the scrollbar, shrinks, dismisses it, grows, and pulses between the
 * two sizes forever. `scrollbar-gutter: stable` keeps that from arising where
 * it is supported; this holds the line where it is not. Room lost is a
 * different matter and always redraws — see the resize handler.
 */
const RESIZE_GROWTH_THRESHOLD_PX = 24;

/**
 * A box to draw over the rendered page. The viewer draws what it is given and
 * decides nothing: which findings become regions, and which one is active, is
 * the calling screen's policy.
 */
export interface ViewerRegion {
  /** The finding this region came from. */
  id: string;
  box: BoundingBox;
  /** The region the reviewer asked for, drawn more strongly than its neighbours. */
  active: boolean;
  /** Accessible name — what a screen reader announces for the box. */
  label: string;
}

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

  /**
   * Boxes to draw over the page. Optional by design: the citation flow passes
   * none, and an empty overlay is a normal state rather than an edge case.
   */
  readonly regions = input<readonly ViewerRegion[]>([]);

  /**
   * Why this page carries no box, when the caller knows. Shown against the
   * rendered page so the reviewer looking at the document gets the same
   * explanation as the one reading the finding.
   */
  readonly notice = input<string | null>(null);

  protected readonly state = signal<ViewerState>('idle');

  /**
   * Whether the canvas holds a page. Until it does it is the browser's default
   * 300x150, and showing that flashes a small white box in place of the page
   * each time a document is opened.
   */
  protected readonly painted = signal(false);

  protected readonly sourceLabels = CITATION_SOURCE_LABELS;

  /** The canvas only exists while the state is 'rendered'. */
  private readonly canvas = viewChild<ElementRef<HTMLCanvasElement>>('pdfCanvas');
  /** The scroll container around the page; it is what the column resizes. */
  private readonly viewport = viewChild<ElementRef<HTMLElement>>('viewport');
  private readonly handle = signal<PdfDocumentHandle | null>(null);
  private resizeObserver: ResizeObserver | null = null;
  private resizeTimer: ReturnType<typeof setTimeout> | null = null;

  /** The page being shown; a citation without a page opens at the first. */
  protected readonly pageNumber = signal(1);

  /** How much larger than the fitted width the page is drawn. */
  protected readonly zoom = signal(1);
  protected readonly zoomLabel = computed(() =>
    this.zoom() === 1 ? 'Fit' : `${Math.round(this.zoom() * 100)}%`,
  );
  protected readonly canZoomIn = computed(() => this.zoom() < ZOOM_LEVELS[ZOOM_LEVELS.length - 1]);
  protected readonly canZoomOut = computed(() => this.zoom() > ZOOM_LEVELS[0]);

  /** Regions whose box belongs to the page currently rendered. */
  protected readonly visibleRegions = computed(() =>
    this.regions().filter((region) => region.box.pageNumber === this.pageNumber()),
  );

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

      // A citation can name a page the document does not have: one an AI
      // produced, or one carried over from a document that has since been
      // re-uploaded shorter. pdf.js rejects such a request outright, so the
      // page asked for is pulled back into the document's own range and the
      // nearest real page is shown — as the old iframe did with a bad #page
      // fragment — rather than failing a document that is perfectly fine.
      const inRange = Math.min(Math.max(page, 1), handle.pageCount);
      if (inRange !== page) {
        // Everything keyed to the page — the label, the boxes drawn — follows
        // the page actually shown. The write re-runs this effect.
        this.pageNumber.set(inRange);
        return;
      }

      // Read inside the effect, not inside the render it starts: a render runs
      // as a later microtask, where a signal read would not be tracked, and a
      // zoom the reviewer asks for has to re-render the page.
      void this.draw(handle, page, canvas, this.renderWidth());
      this.observe(canvas);
    });

    // A finding on the page already shown re-points the boxes without
    // re-rendering anything, so bringing the new one into view has to follow
    // the regions rather than the render.
    effect(() => {
      this.visibleRegions();
      this.revealActiveRegion();
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

  protected zoomIn(): void {
    this.zoom.set(ZOOM_LEVELS.find((level) => level > this.zoom()) ?? this.zoom());
  }

  protected zoomOut(): void {
    this.zoom.set([...ZOOM_LEVELS].reverse().find((level) => level < this.zoom()) ?? this.zoom());
  }

  /**
   * The width to draw the page at: the viewer's own content width, so a fitted
   * page needs no horizontal scrolling, multiplied by the zoom. The overlay's
   * boxes are percentages of the page surface, which wraps the canvas at
   * whatever size this produces, so zooming needs no geometry of its own.
   */
  private renderWidth(): number {
    // jsdom measures nothing; the fallback keeps rendering exercisable there.
    const available = this.viewport()?.nativeElement.clientWidth || 800;
    return Math.round(available * this.zoom());
  }

  /**
   * Puts the box the reviewer asked to see in the middle of the viewer. The
   * page is usually taller than the box showing it — and, zoomed in, wider —
   * so without this the reviewer clicks "View page" and has to hunt for the
   * highlight the click was meant to show them.
   */
  private revealActiveRegion(): void {
    const viewport = this.viewport()?.nativeElement;
    const canvas = this.canvas()?.nativeElement;
    const active = this.visibleRegions().find((region) => region.active);
    if (!viewport || !canvas || !active) {
      return;
    }

    const pageHeight = canvas.clientHeight;
    const pageWidth = canvas.clientWidth;
    if (pageHeight === 0 || pageWidth === 0) {
      // Nothing is laid out yet; the render that follows reveals it instead.
      return;
    }

    const centreY = (active.box.y + active.box.height / 2) * pageHeight;
    const centreX = (active.box.x + active.box.width / 2) * pageWidth;
    // The browser clamps both to what there is to scroll.
    viewport.scrollTop = Math.max(0, centreY - viewport.clientHeight / 2);
    viewport.scrollLeft = Math.max(0, centreX - viewport.clientWidth / 2);
  }

  private loadedDocumentId: string | null = null;

  /**
   * Bumped by every `release()` and every `load()`. A pending request whose
   * captured id no longer matches this counter when it resolves has been
   * superseded by a later target change, so its result is discarded (and any
   * handle it produced is destroyed) instead of overwriting the current one.
   */
  private loadToken = 0;

  private load(caseId: string, documentId: string): void {
    const requestId = ++this.loadToken;
    this.state.set('loading');
    this.documentService.getDocumentContent(caseId, documentId).subscribe({
      next: async (blob) => {
        try {
          const handle = await this.pdf.open(blob);
          if (requestId !== this.loadToken) {
            // A newer target superseded this request while pdf.js was still
            // parsing; close the handle it produced rather than orphaning it
            // or overwriting the handle the current target is showing.
            handle.destroy();
            return;
          }
          this.loadedDocumentId = documentId;
          this.handle.set(handle);
          this.state.set('rendered');
        } catch {
          if (requestId === this.loadToken) {
            // The file arrived but pdf.js could not parse it.
            this.state.set('error');
          }
        }
      },
      error: (error: HttpErrorResponse) => {
        if (requestId === this.loadToken) {
          // 404 covers both "no such document" and "its file is gone"; either
          // way there is nothing for the reviewer to look at here.
          this.state.set(error.status === 404 ? 'unavailable' : 'error');
        }
      },
    });
  }

  /**
   * Renders run one at a time. pdf.js rejects a second render against a canvas
   * it is already drawing into ("Cannot use the same canvas during multiple
   * render() operations"), and clicking from a finding on one page to a
   * finding on another before the first render settles — the whole point of
   * the feature — is exactly that. Each render therefore waits for the one
   * before it to settle, whatever its outcome.
   */
  private drawQueue: Promise<void> = Promise.resolve();

  /**
   * Bumped by every draw request. A queued render whose token is stale by the
   * time its turn comes has been superseded by a later page, and is dropped.
   */
  private drawToken = 0;

  /** The width the page on screen was drawn at, or null while there is none. */
  private lastRenderWidth: number | null = null;

  private draw(
    handle: PdfDocumentHandle,
    page: number,
    canvas: HTMLCanvasElement,
    width: number,
  ): Promise<void> {
    const token = ++this.drawToken;
    const render = this.drawQueue.then(() => this.render(handle, page, canvas, token, width));
    // The queue itself must never hold a rejection, or one unexpected failure
    // would leave every later render waiting on a promise that never settles
    // into a `then`.
    this.drawQueue = render.catch(() => undefined);
    return render;
  }

  private async render(
    handle: PdfDocumentHandle,
    page: number,
    canvas: HTMLCanvasElement,
    token: number,
    width: number,
  ): Promise<void> {
    if (token !== this.drawToken || this.handle() !== handle) {
      // Superseded while an earlier render held the canvas: this page is no
      // longer the one being shown, and its document may already be closed.
      // Drawing it would be overwritten at once and failing it would report a
      // problem with a page nobody is looking at, so it is dropped silently —
      // the render that superseded it is the one that sets the state.
      return;
    }

    try {
      await handle.renderPage(page, canvas, width);
      this.painted.set(true);
      this.lastRenderWidth = width;
      this.revealActiveRegion();
    } catch {
      if (token !== this.drawToken || this.handle() !== handle) {
        // Superseded mid-render; the failure belongs to a page already gone, or
        // to a document released while it was drawing — closing a document
        // rejects the render in flight, and that rejection must not overwrite
        // the state the close itself set.
        return;
      }

      this.state.set('error');
      // Forget which document is open so that turning to another page reloads
      // it rather than short-circuiting straight back into this failed state.
      this.loadedDocumentId = null;
    }
  }

  /**
   * A canvas rendered at one width and scaled by CSS goes soft, so the page is
   * re-rendered when the viewport changes size. The overlay needs no such
   * handling: its boxes are percentages of the page surface.
   */
  private observe(canvas: HTMLCanvasElement): void {
    const container = this.viewport()?.nativeElement ?? null;
    if (this.resizeObserver !== null || container === null) {
      return;
    }

    // Absent in non-browser test environments; the viewer still renders once.
    if (typeof ResizeObserver === 'undefined') {
      return;
    }

    // A ResizeObserver delivers one notification the instant observation
    // begins, even though nothing has resized. Reacting to it would
    // re-render every document on open or page turn, and risk that render
    // racing the one the effect above already triggered on the same canvas.
    let sawInitialNotification = false;

    this.resizeObserver = new ResizeObserver(() => {
      if (!sawInitialNotification) {
        sawInitialNotification = true;
        return;
      }

      // A drag fires this per frame; re-render once it settles.
      if (this.resizeTimer !== null) {
        clearTimeout(this.resizeTimer);
      }

      this.resizeTimer = setTimeout(() => {
        const handle = this.handle();
        // A container mid-collapse (e.g. its column being dragged shut)
        // reports zero width; there is nothing useful to render there.
        if (handle === null || container.clientWidth <= 0) {
          return;
        }

        // A container that has narrowed is redrawn however slightly it moved:
        // the page is sized in pixels, so anything else leaves it spilling out
        // of the box. Room gained is only worth a redraw when there is a real
        // gain in sharpness to be had — growing back into the space a
        // disappearing scrollbar just released only summons it again.
        const width = this.renderWidth();
        const last = this.lastRenderWidth;
        if (last !== null && width >= last && width - last < RESIZE_GROWTH_THRESHOLD_PX) {
          return;
        }

        void this.draw(handle, this.pageNumber(), canvas, width);
      }, 150);
    });
    this.resizeObserver.observe(container);
  }

  private stopObserving(): void {
    if (this.resizeTimer !== null) {
      clearTimeout(this.resizeTimer);
      this.resizeTimer = null;
    }

    this.resizeObserver?.disconnect();
    this.resizeObserver = null;
  }

  /** Closes the open document so a long review session does not leak memory. */
  private release(): void {
    this.stopObserving();
    // Invalidate any request still in flight so a late response cannot
    // resurrect a handle for a target this viewer has moved past.
    this.loadToken++;
    this.handle()?.destroy();
    this.handle.set(null);
    this.loadedDocumentId = null;
    // Nothing is on screen: no width for a later resize to compare against,
    // and the next document gets a canvas of its own to fill before it shows.
    this.lastRenderWidth = null;
    this.painted.set(false);
  }
}
