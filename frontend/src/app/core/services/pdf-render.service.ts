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
    this.installWorker(pdfjs.GlobalWorkerOptions, pdfjs.version);

    // getDocument does not take a Blob; hand it the bytes.
    const data = new Uint8Array(await file.arrayBuffer());
    const loadingTask = pdfjs.getDocument({ data });
    const document = await loadingTask.promise;

    /**
     * Renders through this handle run one at a time. pdf.js throws "Cannot use
     * the same canvas during multiple render() operations" when a second
     * render starts against a canvas the first is still drawing into, and the
     * `canvas.width` assignment below would in any case blank what the first
     * render had already painted. A caller may therefore ask for a new page
     * whenever it likes without tracking what is still in flight.
     */
    let queue: Promise<unknown> = Promise.resolve();

    /**
     * Pages are drawn here first, then handed to the on-screen canvas in one
     * step. Assigning `canvas.width` clears it, so rendering straight into the
     * canvas the reviewer is looking at blanks the page for as long as the
     * render takes — clicking from finding to finding, that reads as a flicker.
     * One buffer serves the whole document: renders through a handle are
     * serialized, so only one page is ever being drawn into it.
     */
    let buffer: HTMLCanvasElement | null = null;

    const drawPage = async (
      pageNumber: number,
      canvas: HTMLCanvasElement,
      cssWidth: number,
    ): Promise<RenderedPageSize> => {
      // The destination context is taken up front so a canvas that cannot be
      // drawn into fails loudly before a page is rendered for it.
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

      const width = Math.round(viewport.width);
      const height = Math.round(viewport.height);
      buffer ??= canvas.ownerDocument.createElement('canvas');
      buffer.width = width;
      buffer.height = height;

      // Rendering targets a canvas in this pdf.js version's RenderParameters
      // (canvasContext is retained only for backwards compatibility).
      await page.render({ canvas: buffer, viewport }).promise;

      const size: RenderedPageSize = {
        width: Math.round(width / ratio),
        height: Math.round(height / ratio),
      };
      // Resize and paint in one go. The canvas holds the previous page until
      // this point and the new one from here on; it is never blank, and never
      // sized for a page that has not been drawn onto it yet.
      canvas.width = width;
      canvas.height = height;
      canvas.style.width = `${size.width}px`;
      canvas.style.height = `${size.height}px`;
      context.drawImage(buffer, 0, 0);
      return size;
    };

    return {
      pageCount: document.numPages,
      renderPage: (pageNumber, canvas, cssWidth) => {
        const render = queue.then(() => drawPage(pageNumber, canvas, cssWidth));
        // The queue holds the outcome of the last render but never its
        // rejection: a failed page must not stop the next one from starting.
        queue = render.catch(() => undefined);
        return render;
      },
      destroy: () => {
        // A page-sized bitmap is worth releasing rather than waiting for the
        // closure to be collected.
        if (buffer !== null) {
          buffer.width = 0;
          buffer.height = 0;
          buffer = null;
        }

        // PDFDocumentProxy (the object `loadingTask.promise` resolves to) has
        // no destroy() of its own in this pdf.js version — only cleanup(),
        // which frees caches but leaves the worker running. Full teardown,
        // including terminating the worker, lives on the loading task.
        void loadingTask.destroy();
      },
    };
  }

  /**
   * pdf.js parses on a worker thread. `angular.json` copies
   * `pdf.worker.min.mjs` next to the built app (see the `assets` entry
   * pointing at `node_modules/pdfjs-dist/build`) and `workerSrc` is pointed
   * at that path, rather than using `new Worker(new URL(...))`: the esbuild
   * bundler Angular 22 uses does not resolve a bare `pdfjs-dist/...`
   * specifier there — it treats the string as a path relative to this file
   * and fails to find it, so the module-worker form does not bundle.
   *
   * Two details keep that copy working in production, where the app is served
   * from a static host under a base href rather than from `ng serve`:
   *
   * - The path is resolved against `document.baseURI` here rather than left
   *   for the browser to resolve when it constructs the worker, so a build
   *   deployed under `--base-href` does not depend on pdf.js and the browser
   *   agreeing on what the relative string means.
   * - It carries the version of the pdf.js that is asking for it. Assets are
   *   copied unhashed while the app bundle is hashed, so without this a
   *   browser holding a cached worker from an earlier release would pair it
   *   with a newer API and throw "The API version does not match the Worker
   *   version" on every document. Taking the version from the library itself
   *   keeps the two in step with no second place to update.
   */
  private installWorker(options: { workerSrc: string }, version: string): void {
    if (this.workerInstalled) {
      return;
    }

    options.workerSrc = new URL(`pdf.worker.min.mjs?v=${version}`, document.baseURI).href;
    this.workerInstalled = true;
  }
}
