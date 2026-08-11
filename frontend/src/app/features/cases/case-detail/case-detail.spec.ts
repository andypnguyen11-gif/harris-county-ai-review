import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { CaseService } from '../../../core/services/case.service';
import { DocumentService } from '../../../core/services/document.service';
import { ValidationService } from '../../../core/services/validation.service';
import { DocumentUpload } from '../../document-upload/document-upload';
import { makeCase } from '../../../testing/case-fixtures';
import { makeDocument } from '../../../testing/document-fixtures';
import { makeValidationReport } from '../../../testing/validation-fixtures';
import { CaseDetail } from './case-detail';

describe('CaseDetail', () => {
  let getCase: ReturnType<typeof vi.fn>;
  let getDocuments: ReturnType<typeof vi.fn>;
  let getLatestReport: ReturnType<typeof vi.fn>;

  function setup(id: string): void {
    getDocuments ??= vi.fn(() => of([]));
    getLatestReport ??= vi.fn(() =>
      throwError(() => new HttpErrorResponse({ status: 404, statusText: 'Not Found' })),
    );
    TestBed.configureTestingModule({
      imports: [CaseDetail],
      providers: [
        provideRouter([]),
        { provide: CaseService, useValue: { getCase } },
        { provide: DocumentService, useValue: { getDocuments, uploadDocument: vi.fn() } },
        {
          provide: ValidationService,
          useValue: { getLatestReport, runValidation: vi.fn() },
        },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: new Map([['id', id]]) } },
        },
      ],
    });
  }

  afterEach(() => {
    getDocuments = undefined as unknown as ReturnType<typeof vi.fn>;
    getLatestReport = undefined as unknown as ReturnType<typeof vi.fn>;
  });

  async function render() {
    const fixture = TestBed.createComponent(CaseDetail);
    await fixture.whenStable();
    return fixture;
  }

  it('loads the case by route id and renders header, metadata, and sections', async () => {
    const caseItem = makeCase({
      name: 'Cypress Creek Culvert',
      caseNumber: 'HC-2026-0042',
      status: 'ReadyForReview',
    });
    getCase = vi.fn(() => of(caseItem));
    setup(caseItem.id);
    const fixture = await render();
    const el = fixture.nativeElement as HTMLElement;

    expect(getCase).toHaveBeenCalledWith(caseItem.id);
    expect(el.querySelector('h1')?.textContent).toContain('Cypress Creek Culvert');
    expect(el.textContent).toContain('HC-2026-0042');
    expect(el.textContent).toContain('Ready for Review');
    expect(el.textContent).toContain('Floodplain Development Permit');
    expect(el.textContent).toContain('Documents');
    expect(el.textContent).toContain('Validation');
  });

  it('shows an error state when the case cannot be loaded', async () => {
    getCase = vi.fn(() => throwError(() => new Error('not found')));
    setup('missing-id');
    const fixture = await render();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Unable to load case');
  });

  it('lists the case documents with type and status', async () => {
    const caseItem = makeCase();
    getCase = vi.fn(() => of(caseItem));
    getDocuments = vi.fn(() =>
      of([
        makeDocument({
          caseId: caseItem.id,
          fileName: 'elevation-certificate.pdf',
          documentType: 'ElevationCertificate',
          processingStatus: 'Uploaded',
        }),
        makeDocument({
          caseId: caseItem.id,
          fileName: 'drainage.pdf',
          documentType: 'DrainagePlan',
          processingStatus: 'Failed',
        }),
      ]),
    );
    setup(caseItem.id);
    const fixture = await render();
    const el = fixture.nativeElement as HTMLElement;

    expect(getDocuments).toHaveBeenCalledWith(caseItem.id);
    const rows = el.querySelectorAll('.document-table tbody tr');
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('elevation-certificate.pdf');
    expect(rows[0].textContent).toContain('Elevation Certificate');
    expect(rows[0].textContent).toContain('Uploaded');
    expect(rows[1].textContent).toContain('drainage.pdf');
    expect(rows[1].textContent).toContain('Failed');
  });

  it('shows an empty state and the upload component when the case has no documents', async () => {
    const caseItem = makeCase();
    getCase = vi.fn(() => of(caseItem));
    setup(caseItem.id);
    const fixture = await render();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('No documents have been uploaded yet.');
    expect(el.querySelector('app-document-upload')).not.toBeNull();
    expect(el.querySelector('.dropzone')).not.toBeNull();
  });

  it('shows a documents error state with retry when the list cannot be loaded', async () => {
    const caseItem = makeCase();
    getCase = vi.fn(() => of(caseItem));
    getDocuments = vi
      .fn()
      .mockReturnValueOnce(throwError(() => new Error('boom')))
      .mockReturnValueOnce(of([makeDocument({ caseId: caseItem.id, fileName: 'late.pdf' })]));
    setup(caseItem.id);
    const fixture = await render();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('The documents for this case could not be loaded.');

    const retry = [...el.querySelectorAll('button')].find(
      (candidate) => candidate.textContent?.trim() === 'Retry',
    );
    expect(retry).toBeDefined();
    retry!.click();
    await fixture.whenStable();

    expect(getDocuments).toHaveBeenCalledTimes(2);
    expect(el.textContent).toContain('late.pdf');
  });

  it('embeds the validation report panel for the case', async () => {
    const caseItem = makeCase();
    getCase = vi.fn(() => of(caseItem));
    getLatestReport = vi.fn(() =>
      of(
        makeValidationReport({
          caseId: caseItem.id,
          items: [],
        }),
      ),
    );
    setup(caseItem.id);
    const fixture = await render();
    const el = fixture.nativeElement as HTMLElement;

    expect(getLatestReport).toHaveBeenCalledWith(caseItem.id);
    expect(el.querySelector('app-validation-report')).not.toBeNull();
    expect(el.textContent).toContain('Re-run validation');
  });

  it('shows the validation empty state when the case has never been validated', async () => {
    const caseItem = makeCase();
    getCase = vi.fn(() => of(caseItem));
    setup(caseItem.id);
    const fixture = await render();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('No validation report yet.');
    expect(el.textContent).toContain('Run validation');
  });

  it('appends a document to the list when the upload component reports success', async () => {
    const caseItem = makeCase();
    getCase = vi.fn(() => of(caseItem));
    setup(caseItem.id);
    const fixture = await render();

    const upload = fixture.debugElement.query(
      (node) => node.componentInstance instanceof DocumentUpload,
    );
    expect(upload).not.toBeNull();

    const uploadedDocument = makeDocument({ caseId: caseItem.id, fileName: 'fresh-upload.pdf' });
    (upload!.componentInstance as DocumentUpload).uploaded.emit(uploadedDocument);
    await fixture.whenStable();

    const el = fixture.nativeElement as HTMLElement;
    const rows = el.querySelectorAll('.document-table tbody tr');
    expect(rows).toHaveLength(1);
    expect(rows[0].textContent).toContain('fresh-upload.pdf');
  });
});
