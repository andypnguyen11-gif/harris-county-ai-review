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

  it('fails loudly when the canvas has no 2D context', async () => {
    const canvas = document.createElement('canvas');
    canvas.getContext = vi.fn(() => null) as never;
    const handle = await service.open(new Blob(['%PDF-1.7']));

    await expect(handle.renderPage(1, canvas, 612)).rejects.toThrow(/2D context/);
  });
});
