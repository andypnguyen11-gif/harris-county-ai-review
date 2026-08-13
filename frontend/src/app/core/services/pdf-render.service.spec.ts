import { TestBed } from '@angular/core/testing';

import { PdfRenderService } from './pdf-render.service';

const getViewport = vi.fn();
const render = vi.fn();
const getPage = vi.fn();
const destroy = vi.fn();
const getDocument = vi.fn();
const globalWorkerOptions: { workerSrc: string } = { workerSrc: '' };

vi.mock('pdfjs-dist', () => ({
  GlobalWorkerOptions: globalWorkerOptions,
  getDocument: (...args: unknown[]) => getDocument(...args),
}));

/** Lets every pending microtask run, whatever depth of promise chain. */
function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('PdfRenderService', () => {
  let service: PdfRenderService;

  /** A canvas whose 2D context is a stub — jsdom has no real one. */
  function fakeCanvas(): HTMLCanvasElement {
    const canvas = document.createElement('canvas');
    canvas.getContext = vi.fn(() => ({}) as unknown as CanvasRenderingContext2D) as never;
    return canvas;
  }

  beforeEach(() => {
    globalWorkerOptions.workerSrc = '';
    // A US Letter page at 72 DPI, portrait.
    getViewport.mockImplementation(({ scale }: { scale: number }) => ({
      width: 612 * scale,
      height: 792 * scale,
    }));
    render.mockReturnValue({ promise: Promise.resolve() });
    getPage.mockResolvedValue({ getViewport, render });
    getDocument.mockReturnValue({
      promise: Promise.resolve({ numPages: 3, getPage }),
      destroy,
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

  it('installs the worker source once, not per open', async () => {
    await service.open(new Blob(['%PDF-1.7']));
    const first = globalWorkerOptions.workerSrc;
    await service.open(new Blob(['%PDF-1.7']));

    expect(first).not.toBe('');
    expect(globalWorkerOptions.workerSrc).toBe(first);
  });

  it('destroys the underlying document so a long session does not leak', async () => {
    const handle = await service.open(new Blob(['%PDF-1.7']));

    handle.destroy();

    expect(destroy).toHaveBeenCalled();
  });

  it('renders one page at a time so two renders cannot share a canvas', async () => {
    // pdf.js throws "Cannot use the same canvas during multiple render()
    // operations" if a second render starts against a canvas the first is
    // still drawing into, and the second render's canvas.width assignment
    // blanks what the first had painted. A caller turning pages faster than
    // they render must not have to know that.
    const finish: Array<() => void> = [];
    render.mockImplementation(() => ({
      promise: new Promise<void>((resolve) => finish.push(resolve)),
    }));
    // These mocks are module-level, so their call counts carry across tests.
    render.mockClear();
    getPage.mockClear();
    const canvas = fakeCanvas();
    const handle = await service.open(new Blob(['%PDF-1.7']));

    const first = handle.renderPage(1, canvas, 612);
    const second = handle.renderPage(2, canvas, 612);
    await flushMicrotasks();

    expect(render).toHaveBeenCalledTimes(1);
    expect(getPage).toHaveBeenCalledTimes(1);

    finish[0]();
    await first;
    await flushMicrotasks();
    expect(render).toHaveBeenCalledTimes(2);

    finish[1]();
    await second;
    expect(getPage.mock.calls).toEqual([[1], [2]]);
  });

  it('starts the next render after one fails rather than stalling on it', async () => {
    getPage.mockRejectedValueOnce(new Error('Invalid page request.'));
    const canvas = fakeCanvas();
    const handle = await service.open(new Blob(['%PDF-1.7']));

    await expect(handle.renderPage(9, canvas, 612)).rejects.toThrow(/Invalid page request/);

    await expect(handle.renderPage(1, canvas, 612)).resolves.toEqual({
      width: 612,
      height: 792,
    });
  });

  it('fails loudly when the canvas has no 2D context', async () => {
    const canvas = document.createElement('canvas');
    canvas.getContext = vi.fn(() => null) as never;
    const handle = await service.open(new Blob(['%PDF-1.7']));

    await expect(handle.renderPage(1, canvas, 612)).rejects.toThrow(/2D context/);
  });
});
