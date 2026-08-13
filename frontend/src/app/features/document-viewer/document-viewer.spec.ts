import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';

import { CitationTarget } from '../../core/models/citation-target.model';
import { BoundingBox } from '../../core/models/validation.model';
import { DocumentService } from '../../core/services/document.service';
import { PdfRenderService } from '../../core/services/pdf-render.service';
import { DocumentViewer, ViewerRegion } from './document-viewer';

describe('DocumentViewer', () => {
  let getDocumentContent: ReturnType<typeof vi.fn>;
  let renderPage: ReturnType<typeof vi.fn>;
  let destroyHandle: ReturnType<typeof vi.fn>;
  let open: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<DocumentViewer>;

  function caseTarget(overrides: Partial<CitationTarget> = {}): CitationTarget {
    return {
      source: 'Case',
      caseId: 'case-1',
      documentId: 'doc-1',
      title: 'application.pdf',
      section: null,
      page: 2,
      sourceUrl: null,
      ...overrides,
    };
  }

  function countyTarget(overrides: Partial<CitationTarget> = {}): CitationTarget {
    return {
      source: 'County',
      caseId: null,
      documentId: 'doc-9',
      title: 'Floodplain Regulations',
      section: 'Section 4.2',
      page: 17,
      sourceUrl: 'https://www.hcfcd.org/regulations',
      ...overrides,
    };
  }

  async function setup(target: CitationTarget | null): Promise<void> {
    TestBed.configureTestingModule({
      imports: [DocumentViewer],
      providers: [
        { provide: DocumentService, useValue: { getDocumentContent } },
        { provide: PdfRenderService, useValue: { open } },
      ],
    });
    fixture = TestBed.createComponent(DocumentViewer);
    fixture.componentRef.setInput('target', target);
    await fixture.whenStable();
  }

  async function setTarget(target: CitationTarget | null): Promise<void> {
    fixture.componentRef.setInput('target', target);
    await fixture.whenStable();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

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

  beforeEach(() => {
    getDocumentContent = vi.fn(() => of(new Blob(['%PDF-1.7'], { type: 'application/pdf' })));
    renderPage = vi.fn(async () => ({ width: 800, height: 1035 }));
    destroyHandle = vi.fn();
    // No explicit PdfDocumentHandle return-type annotation here: the object
    // literal's members are the loosely typed mocks above, and this is only
    // ever consumed through `useValue` (untyped), matching how every other
    // spec in this codebase hands a fake service to `useValue`.
    open = vi.fn(async () => ({
      pageCount: 4,
      renderPage,
      destroy: destroyHandle,
    }));
  });

  it('shows nothing until a source is selected', async () => {
    await setup(null);

    expect(el().querySelector('.document-viewer')).toBeNull();
    expect(getDocumentContent).not.toHaveBeenCalled();
  });

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

  it('keeps the header page badge and the canvas wrapper on separate classes', async () => {
    await setup(caseTarget({ page: 5 }));

    // Regression: the wrapper around the canvas and the header's "Page N"
    // badge once shared the class `document-viewer__page`, so the wrapper's
    // box styling (border, background, max-height) leaked onto the badge.
    const badge = el().querySelector('.document-viewer__page');
    expect(badge?.tagName).toBe('SPAN');
    expect(badge?.textContent).toContain('Page 5');
    expect(badge?.classList.contains('document-viewer__viewport')).toBe(false);

    const wrapper = el().querySelector('.document-viewer__viewport');
    expect(wrapper?.tagName).toBe('DIV');
    expect(wrapper?.querySelector('.document-viewer__canvas')).not.toBeNull();
  });

  it('destroys a handle superseded by a later target instead of orphaning or showing it', async () => {
    // fakeHandle() gives every open() call its own renderPage/destroy mocks
    // so the test can tell which handle the component ultimately keeps.
    function fakeHandle() {
      return {
        pageCount: 4,
        renderPage: vi.fn(async () => ({ width: 800, height: 1035 })),
        destroy: vi.fn(),
      };
    }
    const resolvers: Array<(handle: ReturnType<typeof fakeHandle>) => void> = [];
    open = vi.fn(() => new Promise((resolve) => resolvers.push(resolve)));

    await setup(caseTarget({ documentId: 'doc-1' }));
    // A second, then a third, target arrives while doc-1's open() is still
    // pending — each triggers its own load() before any of them resolve.
    await setTarget(caseTarget({ documentId: 'doc-2' }));
    await setTarget(caseTarget({ documentId: 'doc-3' }));
    expect(resolvers).toHaveLength(3);

    const [doc1Handle, doc2Handle, doc3Handle] = [fakeHandle(), fakeHandle(), fakeHandle()];
    // Resolve in the same order the requests were made, oldest first.
    resolvers[0](doc1Handle);
    resolvers[1](doc2Handle);
    resolvers[2](doc3Handle);
    await fixture.whenStable();
    await fixture.whenStable();

    // The stale responses are closed, not left open and not shown.
    expect(doc1Handle.destroy).toHaveBeenCalled();
    expect(doc2Handle.destroy).toHaveBeenCalled();
    expect(doc1Handle.renderPage).not.toHaveBeenCalled();
    expect(doc2Handle.renderPage).not.toHaveBeenCalled();
    // Only the current target's handle is kept and rendered.
    expect(doc3Handle.destroy).not.toHaveBeenCalled();
    expect(doc3Handle.renderPage).toHaveBeenCalled();
    expect(el().querySelector('.document-viewer__canvas')).not.toBeNull();
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

  it('shows a loading state while the file is in flight', async () => {
    const pending = new Subject<Blob>();
    getDocumentContent = vi.fn(() => pending.asObservable());
    await setup(caseTarget());

    expect(el().querySelector('[role="status"]')?.textContent).toContain('Loading the document');

    pending.next(new Blob(['%PDF-1.7']));
    pending.complete();
    await fixture.whenStable();
    // The signal write that follows the awaited pdf.open() schedules its
    // change detection outside the zone task whenStable() tracks, so the
    // view is not guaranteed to reflect the new state after a single call.
    await fixture.whenStable();

    expect(el().querySelector('.document-viewer__canvas')).not.toBeNull();
  });

  it('links out to a county document instead of fetching it', async () => {
    await setup(countyTarget());

    expect(getDocumentContent).not.toHaveBeenCalled();
    expect(el().querySelector('.document-viewer__canvas')).toBeNull();
    const link = el().querySelector<HTMLAnchorElement>('.document-viewer__external a');
    expect(link?.getAttribute('href')).toBe('https://www.hcfcd.org/regulations#page=17');
    expect(link?.textContent).toContain('Open page 17');
  });

  it('links to a county document without a page fragment when none is cited', async () => {
    await setup(countyTarget({ page: null }));

    const link = el().querySelector<HTMLAnchorElement>('.document-viewer__external a');
    expect(link?.getAttribute('href')).toBe('https://www.hcfcd.org/regulations');
    expect(link?.textContent).toContain('Open the county document');
  });

  it('explains that a county document has no public link rather than showing a dead one', async () => {
    await setup(countyTarget({ sourceUrl: null }));

    expect(el().querySelector('.document-viewer__external a')).toBeNull();
    expect(el().querySelector('.document-viewer__note')?.textContent).toContain(
      'No public link is recorded',
    );
  });

  it('distinguishes the two sources visually', async () => {
    await setup(countyTarget());
    expect(
      el().querySelector('.document-viewer__source')?.classList.contains(
        'document-viewer__source--county',
      ),
    ).toBe(true);

    await setTarget(caseTarget());
    expect(
      el().querySelector('.document-viewer__source')?.classList.contains(
        'document-viewer__source--case',
      ),
    ).toBe(true);
  });

  it('reports a missing file rather than showing a blank frame', async () => {
    getDocumentContent = vi.fn(() =>
      throwError(() => new HttpErrorResponse({ status: 404, statusText: 'Not Found' })),
    );
    await setup(caseTarget());

    const panel = el().querySelector('.state-panel--error');
    expect(panel?.textContent).toContain('The document file is unavailable');
    expect(el().querySelector('.document-viewer__canvas')).toBeNull();
    // The metadata stays on screen so the reviewer can still find the document.
    expect(el().textContent).toContain('application.pdf');
  });

  it('reports a transport failure separately from a missing file', async () => {
    getDocumentContent = vi.fn(() =>
      throwError(() => new HttpErrorResponse({ status: 0, statusText: 'Unknown Error' })),
    );
    await setup(caseTarget());

    expect(el().querySelector('.state-panel--error')?.textContent).toContain(
      'The document could not be loaded',
    );
  });

  it('retries a failed load on request', async () => {
    getDocumentContent = vi.fn(() =>
      throwError(() => new HttpErrorResponse({ status: 500, statusText: 'Server Error' })),
    );
    await setup(caseTarget());
    expect(el().querySelector('.state-panel--error')).not.toBeNull();

    getDocumentContent.mockReturnValue(of(new Blob(['%PDF-1.7'])));
    el().querySelector<HTMLButtonElement>('.state-panel--error button')!.click();
    await fixture.whenStable();

    expect(el().querySelector('.document-viewer__canvas')).not.toBeNull();
  });

  it('explains that a case citation without a case cannot be opened', async () => {
    await setup(caseTarget({ caseId: null }));

    expect(getDocumentContent).not.toHaveBeenCalled();
    expect(el().querySelector('.state-panel--error')?.textContent).toContain(
      'not linked to a case',
    );
  });

  it('emits closed when dismissed', async () => {
    const closed: unknown[] = [];
    await setup(caseTarget());
    fixture.componentInstance.closed.subscribe((value) => closed.push(value));

    el().querySelector<HTMLButtonElement>('.document-viewer__header button')!.click();

    expect(closed).toHaveLength(1);
  });

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

  it('draws only regions whose page matches the rendered page', async () => {
    await setup(caseTarget({ page: 2 }));

    await setRegions([
      region({ id: 'finding-1', box: box({ pageNumber: 2 }) }),
      region({ id: 'finding-2', box: box({ pageNumber: 3 }) }),
    ]);

    const drawn = el().querySelectorAll<HTMLElement>('.document-viewer__region');
    expect(drawn).toHaveLength(1);
    expect(drawn[0].dataset['regionId']).toBe('finding-1');
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

  /**
   * A stub that mimics a real ResizeObserver: it records the callback and,
   * like the browser API, delivers one notification the moment `.observe()`
   * is called even though nothing has actually resized yet. The component
   * must ignore that first delivery (see the dedicated test below) so a
   * genuine resize is only whatever the test fires afterward.
   */
  function fakeResizeObserverClass(): {
    ctor: new (callback: () => void) => { observe(): void; disconnect(): void };
    notifiers: Array<() => void>;
  } {
    const notifiers: Array<() => void> = [];
    class FakeResizeObserver {
      constructor(private readonly callback: () => void) {}
      observe(): void {
        notifiers.push(this.callback);
        this.callback();
      }
      disconnect(): void {}
    }
    return { ctor: FakeResizeObserver, notifiers };
  }

  it('re-renders the page when its container is resized', async () => {
    const { ctor, notifiers } = fakeResizeObserverClass();
    vi.stubGlobal('ResizeObserver', ctor);
    // Restricted to the timer APIs this component's debounce actually uses.
    // vitest's default toFake set (determined empirically — every other
    // faked API was ruled out individually) also fakes requestAnimationFrame,
    // which Angular's zoneless whenStable() relies on to settle; faking it
    // alongside setTimeout hangs the harness rather than the debounce this
    // test is driving.
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });

    await setup(caseTarget());
    const wrapper = el().querySelector('.document-viewer__viewport') as HTMLElement;
    Object.defineProperty(wrapper, 'clientWidth', { value: 400, configurable: true });
    const initial = renderPage.mock.calls.length;

    // notifiers[0] already fired once as the observe()-time notification
    // above; this is the genuine resize.
    notifiers.forEach((notify) => notify());
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

  it('ignores the notification ResizeObserver delivers when observation begins', async () => {
    // Per spec, a real ResizeObserver fires once immediately on the first
    // observe() call even though nothing has resized. Treating that as a
    // real resize would re-render every document on open or page turn, and
    // risk a second render racing the one the effect already triggered.
    const { ctor } = fakeResizeObserverClass();
    vi.stubGlobal('ResizeObserver', ctor);
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });

    await setup(caseTarget());
    const wrapper = el().querySelector('.document-viewer__viewport') as HTMLElement;
    // A valid width, so only the initial-notification guard (not the
    // zero-width guard) is what suppresses the render in this test.
    Object.defineProperty(wrapper, 'clientWidth', { value: 400, configurable: true });
    const initial = renderPage.mock.calls.length;

    vi.advanceTimersByTime(200);
    await fixture.whenStable();

    expect(renderPage.mock.calls.length).toBe(initial);
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('does not re-render when the container has collapsed to zero width', async () => {
    const { ctor, notifiers } = fakeResizeObserverClass();
    vi.stubGlobal('ResizeObserver', ctor);
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });

    await setup(caseTarget());
    // jsdom reports every element's clientWidth as 0 by default; left
    // unstubbed here, that stands in for a container genuinely collapsed to
    // zero width.
    const initial = renderPage.mock.calls.length;

    notifiers.forEach((notify) => notify());
    vi.advanceTimersByTime(200);
    await fixture.whenStable();

    expect(renderPage.mock.calls.length).toBe(initial);
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });
});
