# Document Region Highlighting — Frontend Implementation Plan (PR-B)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render submitted PDFs with pdf.js beside the validation report and draw a box over the page region a finding came from.

**Architecture:** A single injectable service owns every `pdfjs-dist` import and hands the viewer a small handle (`open` → `renderPage`/`destroy`), so components stay mockable and the library lazy-loads on first use. `DocumentViewer` swaps its `<iframe>` for a `<canvas>` plus an absolutely positioned sibling overlay whose boxes are placed with **percentage** geometry — the normalized 0–1 contract from PR-A maps to CSS percentages with a single `× 100`, so a resize re-renders only the canvas and never recomputes a box. `ValidationReportPanel` owns the draw policy and passes the viewer a finished list; the viewer decides nothing.

**Tech Stack:** Angular 22.1 (standalone components, signals, `input()`/`output()`), TypeScript ~6.0, vitest 4 + jsdom, SCSS, `pdfjs-dist`.

**Spec:** `docs/architecture/document-region-highlighting.md`, PR-B section. Read the [Draw policy](../architecture/document-region-highlighting.md#draw-policy) and [Viewer contract](../architecture/document-region-highlighting.md#viewer-contract) sections before starting — they are locked decisions, not suggestions.

**Depends on:** PR-A (`feature/document-region-coordinates`), which serves `boundingBox` on each validation report item as `{ pageNumber, x, y, width, height }`, camelCased, nullable. This branch is cut from it.

## Global Constraints

- **Frontend only.** No file under `backend/` may be created, modified, or deleted. Verify with `git diff --name-only` before every commit.
- **`pdfjs-dist` is imported in exactly one file:** `frontend/src/app/core/services/pdf-render.service.ts`. No component, template, or test may import it directly. A test that needs pdf.js behavior mocks `PdfRenderService` through Angular DI.
- **Coordinates are fractions of the page in `[0, 1]`, origin top-left.** The frontend multiplies by rendered size and never converts units, never reads a length unit, and never re-normalizes.
- **Tests run in jsdom under vitest**, not a browser. `HTMLCanvasElement.getContext` returns null there and `ResizeObserver` does not exist — both must be mocked or guarded. Never add a browser-mode test runner; `--browsers` fails on this repo because no vitest browser provider is installed.
- **Angular 22 signal idiom, matching the existing code:** `input()`, `input.required()`, `output()`, `signal()`, `computed()`, `viewChild()`, `inject()`. No decorators for inputs/outputs, no constructor injection, no `@ViewChild`.
- **Every existing `ViewerState` value and its user-facing message is preserved:** `idle`, `loading`, `rendered`, `external`, `unavailable`, `error`. County documents keep their external-link behavior and are never fetched or rendered.
- **Opening a page with zero boxes is a normal, tested state**, not an edge case — it is the question-answering citation path.
- **Complete findings get no box in v1.** Their region is still received and available; nothing draws it.
- **No invented regions.** A finding without a box gets a message, never a guessed rectangle.
- Commit messages describe the change itself: **no PR numbers, no task numbers, no AI attribution** (no `Co-Authored-By`, no "Generated with"). This repository is public and reviewed by Harris County.
- Definition of Done for every task: `npm test` and `npm run build` green from `frontend/`.

## File Structure

| File | Responsibility |
|---|---|
| `core/services/pdf-render.service.ts` *(create)* | The only `pdfjs-dist` importer. Opens a blob, renders a page to a canvas, reports CSS dimensions, destroys the document. |
| `core/services/pdf-render.service.spec.ts` *(create)* | Worker wiring, scale arithmetic, lifecycle — with `pdfjs-dist` mocked. |
| `core/models/validation.model.ts` *(modify)* | Mirrors PR-A's DTO: `BoundingBox` plus `boundingBox` on `ValidationReportItem`. |
| `app/testing/validation-fixtures.ts` *(modify)* | Fixture default for the new field. |
| `features/document-viewer/document-viewer.ts` *(modify)* | Canvas rendering, optional region input, resize handling. Owns no draw policy. |
| `features/document-viewer/document-viewer.html` *(modify)* | Canvas + overlay markup replacing the `<iframe>`. |
| `features/document-viewer/document-viewer.scss` *(modify)* | Overlay positioning and box styling. |
| `features/document-viewer/document-viewer.spec.ts` *(modify)* | Rendering, overlay geometry, active state, resize, zero-box open. |
| `features/validation-report/validation-report.ts` *(modify)* | Draw policy: which findings become boxes, and which is active. |
| `features/validation-report/validation-report.html` *(modify)* | Two-column layout; the no-region message. |
| `features/validation-report/validation-report.scss` *(modify)* | Grid, sticky viewer column, stacking below 1100px. |
| `features/validation-report/validation-report.spec.ts` *(modify)* | Draw-policy filtering, toggle behavior, layout structure. |

---

### Task 1: The pdf.js adapter service

**Files:**
- Create: `frontend/src/app/core/services/pdf-render.service.ts`
- Create: `frontend/src/app/core/services/pdf-render.service.spec.ts`
- Modify: `frontend/package.json` (adds the `pdfjs-dist` dependency)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  ```ts
  export interface RenderedPageSize { width: number; height: number }   // CSS pixels
  export interface PdfDocumentHandle {
    readonly pageCount: number;
    renderPage(pageNumber: number, canvas: HTMLCanvasElement, cssWidth: number): Promise<RenderedPageSize>;
    destroy(): void;
  }
  @Injectable({ providedIn: 'root' })
  export class PdfRenderService { open(file: Blob): Promise<PdfDocumentHandle> }
  ```

This is the spec's flagged risk — worker wiring under Angular 22's build. Do it first and alone so a bundling problem surfaces before any UI depends on it.

The service loads `pdfjs-dist` through a **dynamic `import()`**, not a top-level one. That serves three purposes at once: the library lazy-loads on first viewer open (the spec's bundle-size mitigation), tests that provide a fake `PdfRenderService` never pull pdf.js into jsdom at all, and `vi.mock('pdfjs-dist', …)` can intercept it in this file's own tests.

- [ ] **Step 1: Install the dependency**

```bash
cd frontend
npm install pdfjs-dist
```

- [ ] **Step 2: Verify the two API points against the installed version**

Do not skip this. pdf.js has changed `render()`'s parameter shape across major versions, and this plan's code must match what is actually on disk.

```bash
cd frontend
node -e "console.log(require('./node_modules/pdfjs-dist/package.json').version)"
grep -n "canvasContext\|interface RenderParameters" -A 12 node_modules/pdfjs-dist/types/src/display/api.d.ts | head -40
ls node_modules/pdfjs-dist/build/ | grep worker
```

Confirm: (a) `RenderParameters` accepts `canvasContext` and `viewport`, and (b) `build/pdf.worker.min.mjs` exists. If `RenderParameters` instead requires a `canvas` property, use that member in Step 4 and note the deviation in your report. If the worker file has a different name, use the real one.

- [ ] **Step 3: Write the failing test**

Create `frontend/src/app/core/services/pdf-render.service.spec.ts`:

```ts
import { TestBed } from '@angular/core/testing';

import { PdfRenderService } from './pdf-render.service';

const getViewport = vi.fn();
const render = vi.fn();
const getPage = vi.fn();
const destroy = vi.fn();
const getDocument = vi.fn();
const globalWorkerOptions: { workerPort: unknown } = { workerPort: null };

vi.mock('pdfjs-dist', () => ({
  GlobalWorkerOptions: globalWorkerOptions,
  getDocument: (...args: unknown[]) => getDocument(...args),
}));

describe('PdfRenderService', () => {
  let service: PdfRenderService;

  /** A canvas whose 2D context is a stub — jsdom has no real one. */
  function fakeCanvas(): HTMLCanvasElement {
    const canvas = document.createElement('canvas');
    canvas.getContext = vi.fn(() => ({}) as unknown as CanvasRenderingContext2D) as never;
    return canvas;
  }

  beforeEach(() => {
    globalWorkerOptions.workerPort = null;
    // A US Letter page at 72 DPI, portrait.
    getViewport.mockImplementation(({ scale }: { scale: number }) => ({
      width: 612 * scale,
      height: 792 * scale,
    }));
    render.mockReturnValue({ promise: Promise.resolve() });
    getPage.mockResolvedValue({ getViewport, render });
    getDocument.mockReturnValue({
      promise: Promise.resolve({ numPages: 3, getPage, destroy }),
    });

    TestBed.configureTestingModule({});
    service = TestBed.inject(PdfRenderService);
  });

  it('reads the page count from the opened document', async () => {
    const handle = await service.open(new Blob(['%PDF-1.7']));

    expect(handle.pageCount).toBe(3);
  });

  it('passes the file to pdf.js as bytes rather than a blob', async () => {
    await service.open(new Blob(['%PDF-1.7']));

    const [params] = getDocument.mock.calls[0] as [{ data: Uint8Array }];
    expect(params.data).toBeInstanceOf(Uint8Array);
  });

  it('sizes the canvas in device pixels and reports its CSS size', async () => {
    // 2x display: the backing store doubles, the CSS box does not.
    vi.spyOn(window, 'devicePixelRatio', 'get').mockReturnValue(2);
    const canvas = fakeCanvas();
    const handle = await service.open(new Blob(['%PDF-1.7']));

    const size = await handle.renderPage(2, canvas, 306);

    // 306 CSS px wide against a 612pt page is a scale of 0.5; 792 * 0.5 = 396.
    expect(size).toEqual({ width: 306, height: 396 });
    expect(canvas.width).toBe(612);
    expect(canvas.height).toBe(792);
    expect(canvas.style.width).toBe('306px');
    expect(canvas.style.height).toBe('396px');
  });

  it('renders the requested page', async () => {
    const handle = await service.open(new Blob(['%PDF-1.7']));

    await handle.renderPage(2, fakeCanvas(), 612);

    expect(getPage).toHaveBeenCalledWith(2);
    expect(render).toHaveBeenCalled();
  });

  it('installs a module worker once, not per open', async () => {
    await service.open(new Blob(['%PDF-1.7']));
    const first = globalWorkerOptions.workerPort;
    await service.open(new Blob(['%PDF-1.7']));

    expect(first).not.toBeNull();
    expect(globalWorkerOptions.workerPort).toBe(first);
  });

  it('destroys the underlying document so a long session does not leak', async () => {
    const handle = await service.open(new Blob(['%PDF-1.7']));

    handle.destroy();

    expect(destroy).toHaveBeenCalled();
  });

  it('fails loudly when the canvas has no 2D context', async () => {
    const canvas = document.createElement('canvas');
    canvas.getContext = vi.fn(() => null) as never;
    const handle = await service.open(new Blob(['%PDF-1.7']));

    await expect(handle.renderPage(1, canvas, 612)).rejects.toThrow(/2D context/);
  });
});
```

`vi.mock` is hoisted above the imports by vitest, so the mock is in place before the service's dynamic `import('pdfjs-dist')` resolves. The `devicePixelRatio` spy is what makes the scale arithmetic observable — without it the assertion would pass for a scale of 1 whether or not the code multiplies by the ratio.

- [ ] **Step 4: Run the test to verify it fails**

Run: `cd frontend && npx ng test --watch=false`
Expected: FAIL — `pdf-render.service.ts` does not exist, so the import cannot resolve.

- [ ] **Step 5: Write the service**

Create `frontend/src/app/core/services/pdf-render.service.ts`:

```ts
import { Injectable } from '@angular/core';

/** The size a rendered page occupies on screen, in CSS pixels. */
export interface RenderedPageSize {
  width: number;
  height: number;
}

/** An open PDF. Held by the viewer for as long as it shows that document. */
export interface PdfDocumentHandle {
  readonly pageCount: number;
  /**
   * Draws `pageNumber` into `canvas` at `cssWidth` CSS pixels wide, sizing the
   * backing store for the display's pixel ratio so text stays crisp.
   */
  renderPage(
    pageNumber: number,
    canvas: HTMLCanvasElement,
    cssWidth: number,
  ): Promise<RenderedPageSize>;
  destroy(): void;
}

/**
 * The only place this application touches pdf.js.
 *
 * Components take this service rather than the library so they can be tested
 * without a real PDF engine — jsdom has no canvas to render into. The library
 * is pulled in by a dynamic import so it is fetched the first time a reviewer
 * opens a document rather than on first paint, and so tests that stub this
 * service never load it at all.
 */
@Injectable({ providedIn: 'root' })
export class PdfRenderService {
  private workerInstalled = false;

  async open(file: Blob): Promise<PdfDocumentHandle> {
    const pdfjs = await import('pdfjs-dist');
    this.installWorker(pdfjs.GlobalWorkerOptions);

    // getDocument does not take a Blob; hand it the bytes.
    const data = new Uint8Array(await file.arrayBuffer());
    const document = await pdfjs.getDocument({ data }).promise;

    return {
      pageCount: document.numPages,
      renderPage: async (pageNumber, canvas, cssWidth) => {
        const context = canvas.getContext('2d');
        if (context === null) {
          throw new Error('The canvas has no 2D context to render into.');
        }

        const page = await document.getPage(pageNumber);
        const unscaled = page.getViewport({ scale: 1 });
        // The page is drawn to fill the width it was given; its height follows.
        const scale = cssWidth / unscaled.width;
        const ratio = window.devicePixelRatio || 1;
        const viewport = page.getViewport({ scale: scale * ratio });

        canvas.width = Math.round(viewport.width);
        canvas.height = Math.round(viewport.height);

        const size: RenderedPageSize = {
          width: Math.round(viewport.width / ratio),
          height: Math.round(viewport.height / ratio),
        };
        canvas.style.width = `${size.width}px`;
        canvas.style.height = `${size.height}px`;

        await page.render({ canvasContext: context, viewport }).promise;
        return size;
      },
      destroy: () => {
        void document.destroy();
      },
    };
  }

  /**
   * pdf.js parses on a worker thread. Handing it a `new Worker(new URL(...))`
   * is the form the bundler can follow, so the worker file is emitted as a
   * build artifact rather than fetched from a path guessed at runtime.
   */
  private installWorker(options: { workerPort: Worker | null }): void {
    if (this.workerInstalled) {
      return;
    }

    options.workerPort = new Worker(
      new URL('pdfjs-dist/build/pdf.worker.min.mjs', import.meta.url),
      { type: 'module' },
    );
    this.workerInstalled = true;
  }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `cd frontend && npx ng test --watch=false`
Expected: PASS, including the 205 pre-existing tests.

- [ ] **Step 7: Prove the worker actually bundles**

This is the risk the task exists to retire. A green unit test does not prove it, because the unit test mocks the library.

```bash
cd frontend && npm run build
ls dist/frontend/browser/ | grep -i worker
```

Expected: the build succeeds and a worker chunk is emitted. **If no worker chunk appears or the build errors on the `new URL(...)` reference**, fall back to the spec's documented alternative — copy the worker into `public/` and point `GlobalWorkerOptions.workerSrc` at it:

```jsonc
// angular.json, build options — add alongside the existing public glob
{
  "glob": "pdf.worker.min.mjs",
  "input": "node_modules/pdfjs-dist/build",
  "output": "."
}
```

```ts
// pdf-render.service.ts — replace installWorker's body with:
options.workerSrc = 'pdf.worker.min.mjs';
```

(The `workerPort` assertion in the spec then becomes a `workerSrc` assertion.) Record which path you took in your report — later tasks do not care, but the reviewer does.

- [ ] **Step 8: Commit**

```bash
git add frontend/package.json frontend/package-lock.json \
        frontend/src/app/core/services/pdf-render.service.ts \
        frontend/src/app/core/services/pdf-render.service.spec.ts
# plus frontend/angular.json if you took the fallback path
git commit -m "Add a pdf.js rendering service"
```

---

### Task 2: Render the page to a canvas in the viewer

**Files:**
- Modify: `frontend/src/app/features/document-viewer/document-viewer.ts`
- Modify: `frontend/src/app/features/document-viewer/document-viewer.html:32-46`
- Modify: `frontend/src/app/features/document-viewer/document-viewer.scss:57-64`
- Modify: `frontend/src/app/features/document-viewer/document-viewer.spec.ts`

**Interfaces:**
- Consumes: `PdfRenderService`, `PdfDocumentHandle`, `RenderedPageSize` (Task 1).
- Produces: a `<canvas class="document-viewer__canvas">` inside `<div class="document-viewer__page">` in the `rendered` state. The `.document-viewer__frame` iframe and its `#page=` fragment are gone.

The component keeps its existing state machine and every message in it. What changes is only how the `rendered` state draws: a canvas rendered by the service instead of an iframe pointed at an object URL.

The object URL goes away entirely — pdf.js takes the blob directly — so `release()` now destroys the handle rather than revoking a URL.

- [ ] **Step 1: Write the failing tests**

In `document-viewer.spec.ts`, add a fake service and register it. Replace the existing `setup` provider list, and add these tests. **Delete the three iframe-era tests that no longer describe the component**: `renders a case document and fetches it from its case` is rewritten below, and `opens the rendered document at the cited page` plus `renders without a page fragment when the citation names no page` are replaced by the page-number tests here. Also rewrite `releases the previous file when the source changes`, since there is no object URL to revoke.

```ts
import { PdfDocumentHandle, PdfRenderService } from '../../core/services/pdf-render.service';

// ...inside describe('DocumentViewer'):
let renderPage: ReturnType<typeof vi.fn>;
let destroyHandle: ReturnType<typeof vi.fn>;
let open: ReturnType<typeof vi.fn>;

beforeEach(() => {
  getDocumentContent = vi.fn(() => of(new Blob(['%PDF-1.7'], { type: 'application/pdf' })));
  renderPage = vi.fn(async () => ({ width: 800, height: 1035 }));
  destroyHandle = vi.fn();
  open = vi.fn(
    async (): Promise<PdfDocumentHandle> => ({
      pageCount: 4,
      renderPage,
      destroy: destroyHandle,
    }),
  );
});

// ...and in setup()'s providers:
providers: [
  { provide: DocumentService, useValue: { getDocumentContent } },
  { provide: PdfRenderService, useValue: { open } },
],
```

```ts
  it('renders a case document to a canvas', async () => {
    await setup(caseTarget());

    expect(getDocumentContent).toHaveBeenCalledWith('case-1', 'doc-1');
    expect(open).toHaveBeenCalled();
    expect(el().querySelector('.document-viewer__canvas')).not.toBeNull();
    expect(el().querySelector('iframe')).toBeNull();
  });

  it('renders the cited page', async () => {
    await setup(caseTarget({ page: 5 }));

    expect(renderPage).toHaveBeenCalledWith(5, expect.anything(), expect.any(Number));
  });

  it('renders the first page when the citation names none', async () => {
    await setup(caseTarget({ page: null }));

    expect(renderPage).toHaveBeenCalledWith(1, expect.anything(), expect.any(Number));
  });

  it('re-renders when the page changes without re-opening the file', async () => {
    await setup(caseTarget({ page: 2 }));
    expect(open).toHaveBeenCalledTimes(1);

    await setTarget(caseTarget({ page: 3 }));

    expect(renderPage).toHaveBeenLastCalledWith(3, expect.anything(), expect.any(Number));
    // Same document — fetching and parsing it again would be wasted work.
    expect(getDocumentContent).toHaveBeenCalledTimes(1);
    expect(open).toHaveBeenCalledTimes(1);
  });

  it('destroys the open document when the source changes', async () => {
    await setup(caseTarget());

    await setTarget(caseTarget({ documentId: 'doc-2' }));

    expect(destroyHandle).toHaveBeenCalled();
    expect(getDocumentContent).toHaveBeenLastCalledWith('case-1', 'doc-2');
  });

  it('destroys the open document on teardown', async () => {
    await setup(caseTarget());

    fixture.destroy();

    expect(destroyHandle).toHaveBeenCalled();
  });

  it('reports an unreadable PDF as an error rather than a blank canvas', async () => {
    open = vi.fn(() => Promise.reject(new Error('Invalid PDF structure')));
    await setup(caseTarget());

    expect(el().querySelector('.state-panel--error')?.textContent).toContain(
      'The document could not be loaded',
    );
    expect(el().querySelector('.document-viewer__canvas')).toBeNull();
  });
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npx ng test --watch=false --project frontend`
Expected: FAIL — no `.document-viewer__canvas` exists and `PdfRenderService` is never called.

- [ ] **Step 3: Replace the iframe in the template**

In `document-viewer.html`, replace the whole `@case ('rendered')` block (lines 32-46) with:

```html
      @case ('rendered') {
        <div class="document-viewer__page">
          <canvas
            #pdfCanvas
            class="document-viewer__canvas"
            [attr.aria-label]="'Page ' + pageNumber() + ' of ' + source.title"
            role="img"
          ></canvas>
        </div>
        <p class="document-viewer__hint">
          @if (source.page !== null) {
            Showing page {{ source.page }}.
          } @else {
            This citation does not name a page.
          }
        </p>
      }
```

- [ ] **Step 4: Render from the component**

Rewrite `document-viewer.ts`. The changes: drop `DomSanitizer`, `SafeResourceUrl`, `fileUrl`, and the object URL; inject `PdfRenderService`; hold the open handle in a signal; and render from a second effect that waits for the canvas to exist.

```ts
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
```

Keep the `ViewerState` union and the class docblock exactly as they are, then:

```ts
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
```

Keep `externalUrl()` and `close()` verbatim. Replace `retry()`, `load()`, and `release()`:

```ts
  protected retry(): void {
    const target = this.target();
    if (target?.source === 'Case' && target.caseId) {
      this.release();
      this.load(target.caseId, target.documentId);
    }
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
```

- [ ] **Step 5: Style the canvas**

In `document-viewer.scss`, replace the `.document-viewer__frame` rule (lines 57-64) with:

```scss
.document-viewer__viewport {
  width: 100%;
  max-height: 70vh;
  overflow: auto;
  border: 1px solid #d1d5db;
  border-radius: 0.375rem;
  background: #f9fafb;
}

/*
 * The page: as wide as the viewport's content box and as tall as the canvas
 * it wraps. Positioned so the overlay inside it covers the page exactly.
 */
.document-viewer__page-surface {
  position: relative;
}

.document-viewer__canvas {
  display: block;
  width: 100%;
  height: auto;
}
```

**Scrolling and positioning must be two different elements.** The viewport
scrolls; the page surface inside it is the positioning ancestor, and Task 3's
overlay hangs off the surface. Do not put `position: relative` on the element
that carries `overflow: auto`: an absolutely positioned child resolves its
percentages against the containing block's padding box, which for a scroll
container is the *visible* area — so every region box would be drawn too high
by the ratio of viewport height to page height, and the boxes would sit still
while the page scrolled underneath them. jsdom never scrolls, so no test in
this plan can catch it; it is a browser-pass check.

(The class is `__viewport`, not `__page` — `.document-viewer__page` is already
the header's page badge.)

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd frontend && npx ng test --watch=false`
Expected: PASS. Pre-existing viewer tests for the external, unavailable, error, retry, and close paths must all still pass untouched — if one fails, you changed behavior the spec said to preserve.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/app/features/document-viewer/
git commit -m "Render case documents with pdf.js instead of an iframe"
```

---

### Task 3: Draw regions over the page

**Files:**
- Modify: `frontend/src/app/core/models/validation.model.ts`
- Modify: `frontend/src/app/testing/validation-fixtures.ts:9-21`
- Modify: `frontend/src/app/features/document-viewer/document-viewer.ts`
- Modify: `frontend/src/app/features/document-viewer/document-viewer.html`
- Modify: `frontend/src/app/features/document-viewer/document-viewer.scss`
- Modify: `frontend/src/app/features/document-viewer/document-viewer.spec.ts`

**Interfaces:**
- Consumes: the canvas markup from Task 2.
- Produces:
  ```ts
  // core/models/validation.model.ts — mirrors PR-A's BoundingBox record
  export interface BoundingBox {
    pageNumber: number;
    x: number; y: number; width: number; height: number;   // fractions in [0, 1]
  }
  // ValidationReportItem gains: boundingBox: BoundingBox | null;

  // features/document-viewer/document-viewer.ts
  export interface ViewerRegion {
    id: string;            // the finding's id; also the DOM data-region-id
    box: BoundingBox;
    active: boolean;
    label: string;         // accessible name
  }
  // DocumentViewer gains:
  //   readonly regions = input<readonly ViewerRegion[]>([]);
  //   readonly notice  = input<string | null>(null);
  ```

Percentages are the whole trick. A box arrives as fractions of the page, and `left: 12%` of a container that *is* the page means the browser recomputes position on resize for free — no `ResizeObserver` maths, no stale coordinates, no redraw loop. Divs rather than a canvas overlay so the boxes can carry CSS transitions and an accessible name.

- [ ] **Step 1: Write the failing tests**

In `document-viewer.spec.ts`:

```ts
import { BoundingBox } from '../../core/models/validation.model';
import { ViewerRegion } from './document-viewer';

// ...inside describe('DocumentViewer'):
function box(overrides: Partial<BoundingBox> = {}): BoundingBox {
  return { pageNumber: 2, x: 0.1, y: 0.2, width: 0.3, height: 0.04, ...overrides };
}

function region(overrides: Partial<ViewerRegion> = {}): ViewerRegion {
  return { id: 'finding-1', box: box(), active: false, label: 'Owner name', ...overrides };
}

async function setRegions(regions: readonly ViewerRegion[]): Promise<void> {
  fixture.componentRef.setInput('regions', regions);
  await fixture.whenStable();
}
```

```ts
  it('draws no overlay boxes when given none', async () => {
    await setup(caseTarget());

    // The citation flow passes no regions; that is a normal open, not an error.
    expect(el().querySelectorAll('.document-viewer__region')).toHaveLength(0);
    expect(el().querySelector('.document-viewer__canvas')).not.toBeNull();
  });

  it('draws one box per region, positioned as a percentage of the page', async () => {
    await setup(caseTarget());

    await setRegions([region()]);

    const drawn = el().querySelectorAll<HTMLElement>('.document-viewer__region');
    expect(drawn).toHaveLength(1);
    // 0.1 of the page width is 10% of the container the page fills.
    expect(drawn[0].style.left).toBe('10%');
    expect(drawn[0].style.top).toBe('20%');
    expect(drawn[0].style.width).toBe('30%');
    expect(drawn[0].style.height).toBe('4%');
  });

  it('draws a box per region and identifies each one', async () => {
    await setup(caseTarget());

    await setRegions([
      region({ id: 'finding-1', box: box({ x: 0.1 }) }),
      region({ id: 'finding-2', box: box({ x: 0.5 }) }),
    ]);

    const drawn = el().querySelectorAll<HTMLElement>('.document-viewer__region');
    expect(drawn).toHaveLength(2);
    expect([...drawn].map((node) => node.dataset['regionId'])).toEqual([
      'finding-1',
      'finding-2',
    ]);
    expect([...drawn].map((node) => node.style.left)).toEqual(['10%', '50%']);
  });

  it('distinguishes the active region from the others', async () => {
    await setup(caseTarget());

    await setRegions([
      region({ id: 'finding-1', active: true }),
      region({ id: 'finding-2', active: false }),
    ]);

    const drawn = el().querySelectorAll<HTMLElement>('.document-viewer__region');
    expect(drawn[0].classList.contains('document-viewer__region--active')).toBe(true);
    expect(drawn[1].classList.contains('document-viewer__region--active')).toBe(false);
  });

  it('names each region for assistive technology', async () => {
    await setup(caseTarget());

    await setRegions([region({ label: 'Owner name is missing' })]);

    expect(
      el().querySelector('.document-viewer__region')?.getAttribute('aria-label'),
    ).toBe('Owner name is missing');
  });

  it('repeats a no-region notice against the rendered page', async () => {
    await setup(caseTarget());

    fixture.componentRef.setInput('notice', "Couldn't locate this field on the page.");
    await fixture.whenStable();

    // The reviewer is looking at the page; the explanation belongs here too,
    // not only beside the finding they clicked.
    expect(el().querySelector('.document-viewer__notice')?.textContent).toContain(
      "Couldn't locate this field on the page.",
    );
  });

  it('shows no notice when there is nothing to explain', async () => {
    await setup(caseTarget());

    expect(el().querySelector('.document-viewer__notice')).toBeNull();
  });

  it('keeps the boxes when the region set changes without a new document', async () => {
    await setup(caseTarget());
    await setRegions([region({ id: 'finding-1' })]);

    await setRegions([region({ id: 'finding-2' })]);

    const drawn = el().querySelectorAll<HTMLElement>('.document-viewer__region');
    expect(drawn).toHaveLength(1);
    expect(drawn[0].dataset['regionId']).toBe('finding-2');
    // Changing which findings are boxed must not refetch or reparse the file.
    expect(open).toHaveBeenCalledTimes(1);
  });
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npx ng test --watch=false`
Expected: FAIL — `ViewerRegion` is not exported and no `.document-viewer__region` element exists.

- [ ] **Step 3: Mirror the contract in the model**

In `validation.model.ts`, add above `ValidationReportItem`:

```ts
/**
 * A region of a document page, as fractions of the page's width and height
 * with the origin at the top-left. Mirrors the API's BoundingBox. Fractions
 * rather than pixels so the same values place a box correctly at any zoom or
 * canvas size — multiply by the rendered dimensions and draw.
 */
export interface BoundingBox {
  pageNumber: number;
  x: number;
  y: number;
  width: number;
  height: number;
}
```

and add the field to `ValidationReportItem`, after `pageNumber`:

```ts
  /** Where on the page the finding came from; null when it could not be located. */
  boundingBox: BoundingBox | null;
```

In `validation-fixtures.ts`, add `boundingBox: null,` to `makeValidationItem`'s defaults, after `pageNumber: null,`. Leaving the default null keeps every existing fixture-based test exercising the no-region path.

- [ ] **Step 4: Add the region input and overlay**

In `document-viewer.ts`, add the import and the exported type above the `ViewerState` union:

```ts
import { BoundingBox } from '../../core/models/validation.model';

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
```

and the input beside `target`:

```ts
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
```

In `document-viewer.html`, wrap the `<canvas>` in the `.document-viewer__page-surface`
block added in Task 2 and put the overlay inside that same wrapper, immediately after
the `<canvas>`. The overlay must be a sibling of the canvas inside the surface — **not**
a child of the scrolling `.document-viewer__viewport`, for the reason given in Task 2's
Step 5:

```html
          <div class="document-viewer__overlay">
            @for (region of regions(); track region.id) {
              <div
                class="document-viewer__region"
                [class.document-viewer__region--active]="region.active"
                [attr.data-region-id]="region.id"
                [attr.aria-label]="region.label"
                role="img"
                [style.left.%]="region.box.x * 100"
                [style.top.%]="region.box.y * 100"
                [style.width.%]="region.box.width * 100"
                [style.height.%]="region.box.height * 100"
              ></div>
            }
          </div>
```

and add the notice immediately after the closing `</div>` of `.document-viewer__page`, before `.document-viewer__hint`:

```html
        @if (notice(); as message) {
          <p class="document-viewer__notice" role="status">{{ message }}</p>
        }
```

In `document-viewer.scss`, append the notice rule to the existing
`.document-viewer__hint, .document-viewer__note` selector list so it reads
`.document-viewer__hint, .document-viewer__note, .document-viewer__notice`, then append:

```scss
/*
 * Sits exactly over the canvas, whose height the page surface takes, so a
 * box's percentage geometry is a percentage of the page itself. A resize
 * re-renders the canvas; the boxes follow with no recomputation.
 *
 * `inset: 0` resolves against `.document-viewer__page-surface`, never against
 * the scrolling viewport — see Task 2, Step 5.
 */
.document-viewer__overlay {
  position: absolute;
  inset: 0;
  pointer-events: none;
}

.document-viewer__region {
  position: absolute;
  border: 2px solid rgb(163 36 44 / 55%);
  border-radius: 2px;
  background: rgb(163 36 44 / 10%);
  transition:
    border-color 120ms ease,
    background-color 120ms ease;
}

.document-viewer__region--active {
  border-color: var(--color-danger);
  border-width: 3px;
  background: rgb(163 36 44 / 22%);
  box-shadow: 0 0 0 2px rgb(255 255 255 / 70%);
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd frontend && npx ng test --watch=false`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/app/core/models/validation.model.ts \
        frontend/src/app/testing/validation-fixtures.ts \
        frontend/src/app/features/document-viewer/
git commit -m "Draw evidence regions over the rendered page"
```

---

### Task 4: Keep the page crisp across resizes

**Files:**
- Modify: `frontend/src/app/features/document-viewer/document-viewer.ts`
- Modify: `frontend/src/app/features/document-viewer/document-viewer.spec.ts`

**Interfaces:**
- Consumes: `draw()` and the canvas view query (Task 2), the overlay (Task 3).
- Produces: no new public surface.

A canvas rendered at one width and then stretched by CSS goes blurry, so the page re-renders when its container changes width. The boxes need nothing — that is the point of percentages. Debounced, because a drag emits a resize per frame and each one is a full page re-render.

jsdom has no `ResizeObserver`, so the test installs one and the component must tolerate its absence rather than throwing.

- [ ] **Step 1: Write the failing test**

In `document-viewer.spec.ts`:

```ts
  it('re-renders the page when its container is resized', async () => {
    const observers: Array<() => void> = [];
    vi.stubGlobal(
      'ResizeObserver',
      class {
        constructor(callback: () => void) {
          observers.push(callback);
        }
        observe(): void {}
        disconnect(): void {}
      },
    );
    vi.useFakeTimers();

    await setup(caseTarget());
    const initial = renderPage.mock.calls.length;

    observers.forEach((notify) => notify());
    vi.advanceTimersByTime(200);
    await fixture.whenStable();

    expect(renderPage.mock.calls.length).toBeGreaterThan(initial);
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('renders without a ResizeObserver rather than failing', async () => {
    vi.stubGlobal('ResizeObserver', undefined);

    await setup(caseTarget());

    expect(el().querySelector('.document-viewer__canvas')).not.toBeNull();
    expect(renderPage).toHaveBeenCalled();
    vi.unstubAllGlobals();
  });
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npx ng test --watch=false`
Expected: FAIL on the first test — nothing observes the container, so no extra render happens.

- [ ] **Step 3: Observe the container**

In `document-viewer.ts`, add these fields:

```ts
  private resizeObserver: ResizeObserver | null = null;
  private resizeTimer: ReturnType<typeof setTimeout> | null = null;
```

At the end of the second effect (the one that draws), after `void this.draw(...)`, add:

```ts
      this.observe(canvas);
```

and add the two methods:

```ts
  /**
   * A canvas rendered at one width and scaled by CSS goes soft, so the page is
   * re-rendered when its container changes size. The overlay needs no such
   * handling: its boxes are percentages of that container.
   */
  private observe(canvas: HTMLCanvasElement): void {
    const container = canvas.parentElement;
    if (this.resizeObserver !== null || container === null) {
      return;
    }

    // Absent in non-browser test environments; the viewer still renders once.
    if (typeof ResizeObserver === 'undefined') {
      return;
    }

    this.resizeObserver = new ResizeObserver(() => {
      // A drag fires this per frame; re-render once it settles.
      if (this.resizeTimer !== null) {
        clearTimeout(this.resizeTimer);
      }

      this.resizeTimer = setTimeout(() => {
        const handle = this.handle();
        if (handle !== null) {
          void this.draw(handle, this.pageNumber(), canvas);
        }
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
```

Call `this.stopObserving();` as the first line of `release()`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd frontend && npx ng test --watch=false`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/app/features/document-viewer/
git commit -m "Re-render the page when the viewer is resized"
```

---

### Task 5: Decide which findings get boxed

**Files:**
- Modify: `frontend/src/app/features/validation-report/validation-report.ts`
- Modify: `frontend/src/app/features/validation-report/validation-report.html:69-91`
- Modify: `frontend/src/app/features/validation-report/validation-report.spec.ts`

**Interfaces:**
- Consumes: `ViewerRegion` and the `regions` input (Task 3), `BoundingBox` on `ValidationReportItem` (Task 3).
- Produces: no new exported surface; `ValidationReportPanel` gains `viewerRegions` (a `computed<ViewerRegion[]>`) and `viewerNotice` (a `computed<string | null>`), both bound to the viewer.

This task implements the spec's [draw policy](../architecture/document-region-highlighting.md#draw-policy) verbatim. Read it before writing code. In short: on open for document `D` at page `N`, box **only** findings with status `Missing`, `Invalid`, or `PotentiallyIncomplete` that belong to `D`, sit on page `N`, and have a region. The clicked finding is active; a second click on it clears the active state while the other boxes stay.

- [ ] **Step 1: Write the failing tests**

`validation-report.spec.ts` already has the helpers you need — `render()`, which
returns a fixture, and `element(fixture)`. It has **no** module-scope `fixture` or
`el()`; do not add one. Note that its `setup()` uses `getLatestReport ??= …`, so
assigning `getLatestReport` **before** calling `render()` is how a test supplies its
report, and `afterEach` resets it.

Extend the import line to `import { BoundingBox, ValidationReport } from '../../core/models/validation.model';`
and add these helpers inside the describe, beside `render()`:

```ts
function box(overrides: Partial<BoundingBox> = {}): BoundingBox {
  return { pageNumber: 1, x: 0.1, y: 0.2, width: 0.3, height: 0.04, ...overrides };
}

/** Renders the panel with `report` already loaded. */
async function openReport(report: ValidationReport) {
  getLatestReport = vi.fn(() => of(report));
  return render();
}

/** Clicks the View page button of the item at `index` in the rendered list. */
function viewPage(fixture: Awaited<ReturnType<typeof render>>, index: number): void {
  element(fixture)
    .querySelectorAll<HTMLButtonElement>('.report-item__reference button')
    [index].click();
}
```

```ts
  it('boxes only the issues on the open page of the open document', async () => {
    const report = makeValidationReport({
      items: [
        makeValidationItem({
          id: 'issue-here',
          status: 'Missing',
          documentId: 'doc-1',
          pageNumber: 1,
          boundingBox: box({ pageNumber: 1 }),
        }),
        makeValidationItem({
          id: 'issue-other-page',
          status: 'Missing',
          documentId: 'doc-1',
          pageNumber: 2,
          boundingBox: box({ pageNumber: 2 }),
        }),
        makeValidationItem({
          id: 'issue-other-document',
          status: 'Invalid',
          documentId: 'doc-2',
          pageNumber: 1,
          boundingBox: box({ pageNumber: 1 }),
        }),
        makeValidationItem({
          id: 'satisfied',
          status: 'Complete',
          documentId: 'doc-1',
          pageNumber: 1,
          boundingBox: box({ pageNumber: 1 }),
        }),
      ],
    });
    const fixture = await openReport(report);

    viewPage(fixture, 0);
    await fixture.whenStable();

    const regions = fixture.componentInstance.viewerRegions();
    // Reviewers are looking for failures: satisfied fields stay unboxed even
    // though their region is present, and other pages and documents are out.
    expect(regions.map((r) => r.id)).toEqual(['issue-here']);
  });

  it('boxes every issue on the page, not only the one clicked', async () => {
    const report = makeValidationReport({
      items: [
        makeValidationItem({
          id: 'first',
          status: 'Missing',
          documentId: 'doc-1',
          pageNumber: 1,
          boundingBox: box(),
        }),
        makeValidationItem({
          id: 'second',
          status: 'PotentiallyIncomplete',
          documentId: 'doc-1',
          pageNumber: 1,
          boundingBox: box({ x: 0.5 }),
        }),
      ],
    });
    const fixture = await openReport(report);

    viewPage(fixture, 0);
    await fixture.whenStable();

    const regions = fixture.componentInstance.viewerRegions();
    expect(regions.map((r) => r.id)).toEqual(['first', 'second']);
    expect(regions.map((r) => r.active)).toEqual([true, false]);
  });

  it('clears the active box when the same finding is clicked again', async () => {
    const report = makeValidationReport({
      items: [
        makeValidationItem({
          id: 'first',
          status: 'Missing',
          documentId: 'doc-1',
          pageNumber: 1,
          boundingBox: box(),
        }),
        makeValidationItem({
          id: 'second',
          status: 'Missing',
          documentId: 'doc-1',
          pageNumber: 1,
          boundingBox: box({ x: 0.5 }),
        }),
      ],
    });
    const fixture = await openReport(report);

    viewPage(fixture, 0);
    await fixture.whenStable();
    viewPage(fixture, 0);
    await fixture.whenStable();

    const regions = fixture.componentInstance.viewerRegions();
    // The active state clears; the page keeps its boxes.
    expect(regions.map((r) => r.active)).toEqual([false, false]);
    expect(regions).toHaveLength(2);
  });

  it('opens a satisfied finding with nothing active', async () => {
    const report = makeValidationReport({
      items: [
        makeValidationItem({
          id: 'satisfied',
          status: 'Complete',
          documentId: 'doc-1',
          pageNumber: 1,
          boundingBox: box(),
        }),
        makeValidationItem({
          id: 'issue',
          status: 'Missing',
          documentId: 'doc-1',
          pageNumber: 1,
          boundingBox: box({ x: 0.5 }),
        }),
      ],
    });
    const fixture = await openReport(report);

    viewPage(fixture, 0);
    await fixture.whenStable();

    const regions = fixture.componentInstance.viewerRegions();
    expect(regions.map((r) => r.id)).toEqual(['issue']);
    expect(regions.some((r) => r.active)).toBe(false);
  });

  it('names each box after the finding it came from', async () => {
    const report = makeValidationReport({
      items: [
        makeValidationItem({
          id: 'first',
          requirement: 'Owner name',
          message: "Field 'owner name' is present but has no value.",
          status: 'Missing',
          documentId: 'doc-1',
          pageNumber: 1,
          boundingBox: box(),
        }),
      ],
    });
    const fixture = await openReport(report);

    viewPage(fixture, 0);
    await fixture.whenStable();

    expect(fixture.componentInstance.viewerRegions()[0].label).toBe(
      "Owner name: Field 'owner name' is present but has no value.",
    );
  });

  it('says so when a finding cannot be located on its page', async () => {
    const report = makeValidationReport({
      items: [
        makeValidationItem({
          status: 'Missing',
          documentId: 'doc-1',
          pageNumber: 1,
          boundingBox: null,
        }),
      ],
    });
    const fixture = await openReport(report);

    expect(element(fixture).querySelector('.report-item__no-region')?.textContent).toContain(
      "Couldn't locate this field on the page",
    );
  });

  it('carries the no-region explanation to the viewer', async () => {
    const report = makeValidationReport({
      items: [
        makeValidationItem({
          status: 'Missing',
          documentId: 'doc-1',
          pageNumber: 1,
          boundingBox: null,
        }),
      ],
    });
    const fixture = await openReport(report);

    viewPage(fixture, 0);
    await fixture.whenStable();

    expect(fixture.componentInstance.viewerNotice()).toBe(
      "Couldn't locate this field on the page.",
    );
  });

  it('shows no viewer notice for a finding that was located', async () => {
    const report = makeValidationReport({
      items: [
        makeValidationItem({
          status: 'Missing',
          documentId: 'doc-1',
          pageNumber: 1,
          boundingBox: box(),
        }),
      ],
    });
    const fixture = await openReport(report);

    viewPage(fixture, 0);
    await fixture.whenStable();

    expect(fixture.componentInstance.viewerNotice()).toBeNull();
  });

  it('draws no boxes when the viewer is closed', async () => {
    const report = makeValidationReport({
      items: [
        makeValidationItem({
          status: 'Missing',
          documentId: 'doc-1',
          pageNumber: 1,
          boundingBox: box(),
        }),
      ],
    });
    const fixture = await openReport(report);

    expect(fixture.componentInstance.viewerRegions()).toEqual([]);
  });
```

`viewerRegions` and `viewerNotice` must be **public** (no `protected`) for these assertions; that is deliberate and worth a comment, since the surrounding members are protected.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npx ng test --watch=false`
Expected: FAIL — `viewerRegions` does not exist.

- [ ] **Step 3: Implement the draw policy**

In `validation-report.ts`, add the imports:

```ts
import { ValidationStatus } from '../../core/models/validation.model';
import { DocumentViewer, ViewerRegion } from '../document-viewer/document-viewer';
```

(`ValidationStatus` joins the existing named imports from `validation.model`; `DocumentViewer` is already imported — add `ViewerRegion` to it.)

Below `PACKAGE_GROUP_LABEL`, add:

```ts
/**
 * The statuses worth boxing. A reviewer opens the page to find what went
 * wrong, so satisfied fields stay unboxed even when their region is known —
 * boxing everything on a dense permit form is noise.
 */
const ISSUE_STATUSES: readonly ValidationStatus[] = ['Missing', 'Invalid', 'PotentiallyIncomplete'];
```

Add the active-finding signal beside `viewerTarget`:

```ts
  /** The finding whose box is drawn active, or null when none is. */
  protected readonly activeItemId = signal<string | null>(null);
```

Add the computed. It is public so tests can read the policy directly rather than inferring it from rendered geometry:

```ts
  /**
   * The boxes the viewer draws for the open document and page, per the draw
   * policy in docs/architecture/document-region-highlighting.md. The viewer
   * draws what it is given; the policy lives here.
   *
   * Public so the policy can be asserted directly in tests.
   */
  readonly viewerRegions = computed<ViewerRegion[]>(() => {
    const target = this.viewerTarget();
    if (target === null || target.page === null) {
      return [];
    }

    const activeId = this.activeItemId();
    return (this.report()?.items ?? [])
      .filter(
        (item) =>
          item.documentId === target.documentId &&
          item.boundingBox !== null &&
          item.boundingBox.pageNumber === target.page &&
          ISSUE_STATUSES.includes(item.status),
      )
      .map((item) => ({
        id: item.id,
        box: item.boundingBox!,
        active: item.id === activeId,
        label: `${item.requirement}: ${item.message}`,
      }));
  });
```

Add the notice beside it. The finding's own message is rendered next to the finding;
this is the same explanation carried to the page the reviewer is looking at:

```ts
  /** Why the open page carries no box for the finding being viewed. */
  readonly viewerNotice = computed<string | null>(() => {
    const activeId = this.activeItemId();
    if (this.viewerTarget() === null || activeId === null) {
      return null;
    }

    const item = (this.report()?.items ?? []).find((candidate) => candidate.id === activeId);
    return item !== undefined && item.boundingBox === null
      ? "Couldn't locate this field on the page."
      : null;
  });
```

Replace `openEvidence` and `closeEvidence`:

```ts
  protected openEvidence(item: ValidationReportItem): void {
    if (!item.documentId) {
      return;
    }

    // A second click on the finding already showing clears its highlight and
    // leaves the page's other boxes in place.
    this.activeItemId.update((current) => (current === item.id ? null : item.id));

    this.viewerTarget.set({
      source: 'Case',
      caseId: this.caseId(),
      documentId: item.documentId,
      title: item.documentType ? DOCUMENT_TYPE_LABELS[item.documentType] : item.requirement,
      section: item.requirement,
      page: item.pageNumber,
      sourceUrl: null,
    });
  }

  protected closeEvidence(): void {
    this.viewerTarget.set(null);
    this.activeItemId.set(null);
  }
```

- [ ] **Step 4: Pass the regions and add the no-region message**

In `validation-report.html`, bind the new input on the viewer (line 91):

```html
    <app-document-viewer
      [target]="viewerTarget()"
      [regions]="viewerRegions()"
      [notice]="viewerNotice()"
      (closed)="closeEvidence()"
    />
```

and inside the `@if (item.documentId)` block, after the closing `</p>` of `.report-item__reference`, add:

```html
                @if (item.boundingBox === null) {
                  <p class="report-item__no-region">
                    Couldn't locate this field on the page.
                  </p>
                }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd frontend && npx ng test --watch=false`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/app/features/validation-report/
git commit -m "Highlight the findings on the page a reviewer opened"
```

---

### Task 6: Put the viewer beside the report

**Files:**
- Modify: `frontend/src/app/features/validation-report/validation-report.html`
- Modify: `frontend/src/app/features/validation-report/validation-report.scss`
- Modify: `frontend/src/app/features/validation-report/validation-report.spec.ts`

**Interfaces:**
- Consumes: everything from Tasks 2-5.
- Produces: no code surface — layout only.

The remaining half of the feature's goal: the reviewer should read a finding and see the page without scrolling away from it. Findings left, viewer sticky right, stacked below 1100px.

Media queries and `position: sticky` do not resolve in jsdom, so the test asserts structure — that the viewer lives in the right-hand column rather than after the findings — and the visual result is confirmed by hand in Step 5.

- [ ] **Step 1: Write the failing test**

In `validation-report.spec.ts`:

```ts
  it('places the viewer in its own column beside the findings', async () => {
    const fixture = await openReport(makeValidationReport());

    const layout = element(fixture).querySelector('.report-layout');
    expect(layout).not.toBeNull();
    expect(layout!.querySelector('.report-layout__findings .report-group')).not.toBeNull();
    expect(layout!.querySelector('.report-layout__viewer app-document-viewer')).not.toBeNull();
    // The viewer must not sit inside the findings column, or it scrolls away
    // with them and the two-column layout is decorative only.
    expect(layout!.querySelector('.report-layout__findings app-document-viewer')).toBeNull();
  });
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && npx ng test --watch=false`
Expected: FAIL — `.report-layout` does not exist.

- [ ] **Step 3: Wrap the two columns**

In `validation-report.html`, wrap the `@for (group of groups(); ...)` block and the `<app-document-viewer>` together. Replace everything from the `@for` line through the `<app-document-viewer .../>` element with:

```html
    <div class="report-layout">
      <div class="report-layout__findings">
        @for (group of groups(); track group.label) {
          <section class="report-group">
            <h3 class="report-group__title">{{ group.label }}</h3>
            <ul class="report-items">
              @for (item of group.items; track item.id) {
                <li class="report-item">
                  <div class="report-item__header">
                    <span class="report-item__requirement">{{ item.requirement }}</span>
                    <app-status-badge [status]="item.status" />
                    <span
                      class="validation-type"
                      [class.validation-type--semantic]="item.validationType === 'Semantic'"
                      title="How this result was produced"
                    >
                      {{ validationTypeLabels[item.validationType] }}
                    </span>
                  </div>
                  @if (item.extractedValue) {
                    <p class="report-item__value">{{ item.extractedValue }}</p>
                  }
                  <p class="report-item__message">{{ item.message }}</p>
                  @if (item.documentId) {
                    <p class="report-item__reference">
                      @if (item.pageNumber) {
                        <span>Page {{ item.pageNumber }}</span>
                      }
                      <button
                        type="button"
                        class="btn btn--secondary btn--small"
                        (click)="openEvidence(item)"
                      >
                        {{ item.pageNumber ? 'View page ' + item.pageNumber : 'View document' }}
                      </button>
                    </p>
                    @if (item.boundingBox === null) {
                      <p class="report-item__no-region">
                        Couldn't locate this field on the page.
                      </p>
                    }
                  } @else if (item.pageNumber) {
                    <p class="report-item__reference">Page {{ item.pageNumber }}</p>
                  }
                </li>
              }
            </ul>
          </section>
        }
      </div>

      <div class="report-layout__viewer">
        <app-document-viewer
          [target]="viewerTarget()"
          [regions]="viewerRegions()"
      [notice]="viewerNotice()"
          (closed)="closeEvidence()"
        />
      </div>
    </div>
```

The item markup is unchanged from what Task 5 left — it moves, it does not change. Diff it against the previous version to be sure nothing was dropped in the move.

- [ ] **Step 4: Add the grid**

In `validation-report.scss`, append:

```scss
/*
 * Findings and evidence side by side so a reviewer reads a finding and sees
 * the page it came from without scrolling between them. The viewer sticks to
 * the top of its column while the findings list scrolls past it.
 */
.report-layout {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 1.5rem;
  align-items: start;
}

.report-layout__findings {
  min-width: 0;
}

.report-layout__viewer {
  position: sticky;
  top: 1rem;
  min-width: 0;
}

/* Below this the two columns are each too narrow to read, so they stack. */
@media (max-width: 1100px) {
  .report-layout {
    grid-template-columns: minmax(0, 1fr);
  }

  .report-layout__viewer {
    position: static;
  }
}
```

The viewer renders nothing when `target()` is null, so the right column is empty until a reviewer opens a document — the findings column keeps its own width either way, and nothing shifts when the viewer appears.

- [ ] **Step 5: Run the tests and check it by hand**

Run: `cd frontend && npx ng test --watch=false && npm run build`
Expected: PASS and a clean build.

Then look at it. Both servers should be running (`docker compose up -d`, the API from `backend/src/HarrisCountyAI.Api`, and `npm start`):

1. Open a case with a validation report at http://localhost:4200.
2. **Re-run extraction on the case first** — documents normalized before PR-A carry null regions and every finding will show "Couldn't locate this field on the page."
3. Click **View page** on a missing-field finding. Confirm: the page renders beside the findings rather than below them, the clicked finding's box is drawn more strongly than the other boxes on that page, and no box is drawn over a satisfied field.
4. Click the same finding again — the strong box drops to normal and the others stay.
5. Narrow the window below 1100px and confirm the columns stack and the page stays crisp.

Capture a screenshot of step 3 for the PR description; the repo requires one for UI changes.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/app/features/validation-report/
git commit -m "Show the document beside the validation findings"
```

---

## Definition of Done

- [ ] `npm test` passes from `frontend/`. Baseline before this branch is **205 passing across 27 files**; report your count against it.
- [ ] `npm run build` succeeds and emits a pdf.js worker chunk.
- [ ] `git diff --name-only feature/document-region-coordinates` lists nothing outside `frontend/` and `docs/`. Compare against **that** branch, not `main` — this one is stacked on it, so a diff against `main` includes all of PR-A's backend files and tells you nothing.
- [ ] No file imports `pdfjs-dist` except `core/services/pdf-render.service.ts`.
- [ ] The viewer's `external`, `unavailable`, `error`, and `idle` states and their messages are unchanged, and their pre-existing tests pass untouched.
- [ ] Opening the viewer with no regions renders a page and no boxes.
- [ ] No commit message references a PR or task number, and none carries AI attribution.

## Notes for the PR description

**Reason for the change.** A finding names a page, but a page of a dense permit form is still a lot of paper to search. PR-A carried the source region through the backend; this draws it, and moves the viewer beside the findings so the reviewer reads and verifies in one place.

**Testing.** Unit tests cover the render service's scale arithmetic and lifecycle, the overlay's percentage geometry, the draw policy's filtering and toggle behavior, and the layout's structure. pdf.js is mocked throughout — no test parses a real PDF. Verified by hand against a re-extracted case; screenshot attached.

**Known limitations.**
- Documents normalized before PR-A carry no regions and show "Couldn't locate this field on the page" until re-extracted. There is no backfill.
- Semantic and comparison findings do not resolve to a single extracted field, so they carry no region and show the same message.
- Satisfied findings are never boxed in v1, though their region is received and available for a later pass.
- A multi-checkbox requirement with nothing checked boxes one arbitrary member of the group, since the finding carries one region while its message speaks about the group.
- Region highlighting applies to case documents only; county reference documents keep their external-link behavior and are not rendered.
