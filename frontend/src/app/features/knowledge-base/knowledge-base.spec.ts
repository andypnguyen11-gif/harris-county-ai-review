import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { KnowledgeBaseService } from '../../core/services/knowledge-base.service';
import { makeKnowledgeDocument } from '../../testing/knowledge-document-fixtures';
import { KnowledgeBase } from './knowledge-base';

describe('KnowledgeBase', () => {
  let getDocuments: ReturnType<typeof vi.fn>;
  let uploadDocument: ReturnType<typeof vi.fn>;
  let deactivateDocument: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<KnowledgeBase>;

  async function setup(): Promise<void> {
    TestBed.configureTestingModule({
      imports: [KnowledgeBase],
      providers: [
        {
          provide: KnowledgeBaseService,
          useValue: { getDocuments, uploadDocument, deactivateDocument },
        },
      ],
    });
    fixture = TestBed.createComponent(KnowledgeBase);
    await fixture.whenStable();
  }

  beforeEach(() => {
    getDocuments = vi.fn(() => of([]));
    uploadDocument = vi.fn();
    deactivateDocument = vi.fn();
  });

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function setInput(selector: string, value: string): void {
    const input = el().querySelector<HTMLInputElement>(selector);
    expect(input).not.toBeNull();
    input!.value = value;
    input!.dispatchEvent(new Event('input'));
  }

  function selectFile(file: File): void {
    const input = el().querySelector<HTMLInputElement>('#kb-file');
    expect(input).not.toBeNull();
    // jsdom does not implement DataTransfer, so stub the FileList directly.
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input!.dispatchEvent(new Event('change'));
  }

  function fillRequiredFields(): void {
    selectFile(new File(['content'], 'regulations.pdf', { type: 'application/pdf' }));
    setInput('#kb-title', 'Floodplain Regulations');
    setInput('#kb-department', 'Harris County Engineering');
    setInput('#kb-permit-type', 'FloodplainDevelopmentPermit');
    setInput('#kb-document-type', 'Regulation');
  }

  function submit(): void {
    el()
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  }

  it('renders a table row per document with metadata, status, and ingestion status', async () => {
    getDocuments = vi.fn(() =>
      of([
        makeKnowledgeDocument({
          title: 'Floodplain Management Regulations',
          department: 'Harris County Engineering',
          permitType: 'FloodplainDevelopmentPermit',
          documentType: 'Regulation',
          effectiveDate: '2024-01-15',
          ingestionStatus: 'Ingested',
        }),
        makeKnowledgeDocument({ title: 'Submittal Checklist', ingestionStatus: 'Failed' }),
      ]),
    );
    await setup();

    const rows = el().querySelectorAll('tbody tr');
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('Floodplain Management Regulations');
    expect(rows[0].textContent).toContain('Harris County Engineering');
    expect(rows[0].textContent).toContain('FloodplainDevelopmentPermit');
    expect(rows[0].textContent).toContain('Regulation');
    expect(rows[0].textContent).toContain('Jan 15, 2024');
    expect(rows[0].textContent).toContain('Active');
    expect(rows[0].textContent).toContain('Ingested');
    expect(rows[1].textContent).toContain('Failed');
  });

  it('marks deactivated documents and hides their deactivate action', async () => {
    getDocuments = vi.fn(() =>
      of([makeKnowledgeDocument({ title: 'Old Regulations', ingestionStatus: 'Deactivated' })]),
    );
    await setup();

    const row = el().querySelector('tbody tr');
    expect(row?.textContent).toContain('Deactivated');
    expect(row?.textContent).not.toContain('Active');
    expect(row?.querySelector('button')).toBeNull();
  });

  it('shows the empty state when there are no documents', async () => {
    await setup();

    expect(el().querySelector('table')).toBeNull();
    expect(el().textContent).toContain('No reference documents yet');
  });

  it('shows an error state and retries loading on demand', async () => {
    getDocuments = vi
      .fn()
      .mockReturnValueOnce(throwError(() => new Error('network')))
      .mockReturnValue(of([makeKnowledgeDocument({ title: 'Recovered Document' })]));
    await setup();

    expect(el().textContent).toContain('Unable to load reference documents');

    el().querySelector<HTMLButtonElement>('.state-panel--error button')?.click();
    await fixture.whenStable();

    expect(getDocuments).toHaveBeenCalledTimes(2);
    expect(el().textContent).toContain('Recovered Document');
  });

  it('reloads with includeDeactivated=true when the toggle is checked', async () => {
    await setup();
    expect(getDocuments).toHaveBeenCalledWith(false);

    const toggle = el().querySelector<HTMLInputElement>('.include-deactivated input');
    expect(toggle).not.toBeNull();
    toggle!.checked = true;
    toggle!.dispatchEvent(new Event('change'));
    await fixture.whenStable();

    expect(getDocuments).toHaveBeenLastCalledWith(true);
  });

  it('shows validation errors and does not upload when required fields are missing', async () => {
    await setup();

    submit();
    await fixture.whenStable();

    expect(uploadDocument).not.toHaveBeenCalled();
    expect(el().textContent).toContain('A file is required.');
    expect(el().textContent).toContain('Title is required.');
    expect(el().textContent).toContain('Department is required.');
    expect(el().textContent).toContain('Permit type is required.');
    expect(el().textContent).toContain('Document type is required.');
  });

  it('uploads the document with form values and reloads the list on success', async () => {
    uploadDocument = vi.fn(() => of(makeKnowledgeDocument()));
    await setup();

    fillRequiredFields();
    setInput('#kb-version', '2024 Edition');
    setInput('#kb-effective-date', '2024-01-15');
    setInput('#kb-source-url', 'https://www.hcfcd.org/regulations');
    submit();
    await fixture.whenStable();

    expect(uploadDocument).toHaveBeenCalledTimes(1);
    const request = uploadDocument.mock.calls[0][0];
    expect(request.file).toBeInstanceOf(File);
    expect(request.file.name).toBe('regulations.pdf');
    expect(request.title).toBe('Floodplain Regulations');
    expect(request.department).toBe('Harris County Engineering');
    expect(request.permitType).toBe('FloodplainDevelopmentPermit');
    expect(request.documentType).toBe('Regulation');
    expect(request.version).toBe('2024 Edition');
    expect(request.effectiveDate).toBe('2024-01-15');
    expect(request.sourceUrl).toBe('https://www.hcfcd.org/regulations');
    expect(getDocuments).toHaveBeenCalledTimes(2);
  });

  it('omits optional fields left blank from the upload request', async () => {
    uploadDocument = vi.fn(() => of(makeKnowledgeDocument()));
    await setup();

    fillRequiredFields();
    submit();
    await fixture.whenStable();

    const request = uploadDocument.mock.calls[0][0];
    expect(request.version).toBeUndefined();
    expect(request.effectiveDate).toBeUndefined();
    expect(request.sourceUrl).toBeUndefined();
  });

  it('shows an upload error and keeps the form values when the service fails', async () => {
    uploadDocument = vi.fn(() => throwError(() => new Error('network')));
    await setup();

    fillRequiredFields();
    submit();
    await fixture.whenStable();

    expect(el().textContent).toContain('The document could not be uploaded.');
    expect(el().querySelector<HTMLInputElement>('#kb-title')?.value).toBe(
      'Floodplain Regulations',
    );
  });

  it('deactivates a document and reloads the list', async () => {
    const document = makeKnowledgeDocument({ title: 'Superseded Guide' });
    getDocuments = vi.fn(() => of([document]));
    deactivateDocument = vi.fn(() => of(undefined));
    await setup();

    el().querySelector<HTMLButtonElement>('tbody button')?.click();
    await fixture.whenStable();

    expect(deactivateDocument).toHaveBeenCalledWith(document.id);
    expect(getDocuments).toHaveBeenCalledTimes(2);
  });

  it('shows a deactivation error when the service fails', async () => {
    getDocuments = vi.fn(() => of([makeKnowledgeDocument()]));
    deactivateDocument = vi.fn(() => throwError(() => new Error('network')));
    await setup();

    el().querySelector<HTMLButtonElement>('tbody button')?.click();
    await fixture.whenStable();

    expect(el().textContent).toContain('The document could not be deactivated.');
    expect(getDocuments).toHaveBeenCalledTimes(1);
  });
});
