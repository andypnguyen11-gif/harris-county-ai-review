import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';

import { CitationTarget } from '../../core/models/citation-target.model';
import { DocumentService } from '../../core/services/document.service';
import { DocumentViewer } from './document-viewer';

describe('DocumentViewer', () => {
  let getDocumentContent: ReturnType<typeof vi.fn>;
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
      providers: [{ provide: DocumentService, useValue: { getDocumentContent } }],
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

  beforeEach(() => {
    getDocumentContent = vi.fn(() => of(new Blob(['%PDF-1.7'], { type: 'application/pdf' })));
  });

  it('shows nothing until a source is selected', async () => {
    await setup(null);

    expect(el().querySelector('.document-viewer')).toBeNull();
    expect(getDocumentContent).not.toHaveBeenCalled();
  });

  it('renders a case document and fetches it from its case', async () => {
    await setup(caseTarget());

    expect(getDocumentContent).toHaveBeenCalledWith('case-1', 'doc-1');
    const frame = el().querySelector<HTMLIFrameElement>('.document-viewer__frame');
    expect(frame).not.toBeNull();
    expect(frame!.getAttribute('title')).toContain('application.pdf');
  });

  it('opens the rendered document at the cited page', async () => {
    await setup(caseTarget({ page: 5 }));

    const frame = el().querySelector<HTMLIFrameElement>('.document-viewer__frame');
    expect(frame!.getAttribute('src')).toContain('#page=5');
    expect(el().querySelector('.document-viewer__hint')?.textContent).toContain('page 5');
  });

  it('renders without a page fragment when the citation names no page', async () => {
    await setup(caseTarget({ page: null }));

    const frame = el().querySelector<HTMLIFrameElement>('.document-viewer__frame');
    expect(frame!.getAttribute('src')).not.toContain('#page=');
    expect(el().querySelector('.document-viewer__hint')?.textContent).toContain(
      'does not name a page',
    );
  });

  it('shows a loading state while the file is in flight', async () => {
    const pending = new Subject<Blob>();
    getDocumentContent = vi.fn(() => pending.asObservable());
    await setup(caseTarget());

    expect(el().querySelector('[role="status"]')?.textContent).toContain('Loading the document');

    pending.next(new Blob(['%PDF-1.7']));
    pending.complete();
    await fixture.whenStable();

    expect(el().querySelector('.document-viewer__frame')).not.toBeNull();
  });

  it('links out to a county document instead of fetching it', async () => {
    await setup(countyTarget());

    expect(getDocumentContent).not.toHaveBeenCalled();
    expect(el().querySelector('.document-viewer__frame')).toBeNull();
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
    expect(el().querySelector('.document-viewer__frame')).toBeNull();
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

    expect(el().querySelector('.document-viewer__frame')).not.toBeNull();
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

  it('releases the previous file when the source changes', async () => {
    const revoke = vi.spyOn(URL, 'revokeObjectURL');
    await setup(caseTarget());

    await setTarget(caseTarget({ documentId: 'doc-2' }));

    expect(revoke).toHaveBeenCalled();
    expect(getDocumentContent).toHaveBeenLastCalledWith('case-1', 'doc-2');
    revoke.mockRestore();
  });
});
