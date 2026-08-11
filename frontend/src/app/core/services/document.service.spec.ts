import { HttpErrorResponse, HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../environments/environment';
import { CaseDocument } from '../models/document.model';
import { makeDocument, makePdfFile } from '../../testing/document-fixtures';
import { DocumentService, DocumentUploadEvent } from './document.service';

describe('DocumentService', () => {
  const caseId = '00000000-0000-0000-0000-000000000123';
  const baseUrl = `${environment.apiUrl}/cases/${caseId}/documents`;
  let service: DocumentService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(DocumentService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('uploadDocument POSTs multipart form data with the file and document type', () => {
    const file = makePdfFile('drainage.pdf');

    service.uploadDocument(caseId, file, 'DrainagePlan').subscribe();

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('POST');
    expect(req.request.reportProgress).toBe(true);

    const body = req.request.body as FormData;
    expect(body).toBeInstanceOf(FormData);
    expect(body.get('documentType')).toBe('DrainagePlan');
    expect((body.get('file') as File).name).toBe('drainage.pdf');

    req.flush(makeDocument());
  });

  it('uploadDocument maps progress events to percentages and the response to a complete event', () => {
    const document = makeDocument({ caseId, fileName: 'site-plan.pdf' });
    const events: DocumentUploadEvent[] = [];

    service
      .uploadDocument(caseId, makePdfFile(), 'SitePlan')
      .subscribe((event) => events.push(event));

    const req = httpMock.expectOne(baseUrl);
    req.event({ type: HttpEventType.UploadProgress, loaded: 25, total: 100 });
    req.event({ type: HttpEventType.UploadProgress, loaded: 60, total: 100 });
    req.flush(document);

    expect(events).toEqual([
      { kind: 'progress', percent: 25 },
      { kind: 'progress', percent: 60 },
      { kind: 'complete', document },
    ]);
  });

  it('uploadDocument reports 0 percent when the total size is unknown', () => {
    const events: DocumentUploadEvent[] = [];

    service
      .uploadDocument(caseId, makePdfFile(), 'SitePlan')
      .subscribe((event) => events.push(event));

    const req = httpMock.expectOne(baseUrl);
    req.event({ type: HttpEventType.UploadProgress, loaded: 512 });
    req.flush(makeDocument());

    expect(events[0]).toEqual({ kind: 'progress', percent: 0 });
  });

  it('uploadDocument propagates HTTP errors to the subscriber', () => {
    let error: HttpErrorResponse | undefined;

    service.uploadDocument(caseId, makePdfFile(), 'SitePlan').subscribe({
      next: () => {
        throw new Error('expected an error, not a value');
      },
      error: (err: HttpErrorResponse) => (error = err),
    });

    httpMock
      .expectOne(baseUrl)
      .flush(
        { title: 'One or more validation errors occurred.', errors: { file: ['too large'] } },
        { status: 400, statusText: 'Bad Request' },
      );

    expect(error).toBeInstanceOf(HttpErrorResponse);
    expect(error?.status).toBe(400);
  });

  it('getDocuments issues GET /cases/{caseId}/documents and returns the list', () => {
    const documents = [makeDocument({ caseId }), makeDocument({ caseId })];
    let result: CaseDocument[] | undefined;

    service.getDocuments(caseId).subscribe((value) => (result = value));

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('GET');
    req.flush(documents);

    expect(result).toEqual(documents);
  });

  it('getDocument issues GET /cases/{caseId}/documents/{documentId} and returns the document', () => {
    const document = makeDocument({ caseId });
    let result: CaseDocument | undefined;

    service.getDocument(caseId, document.id).subscribe((value) => (result = value));

    const req = httpMock.expectOne(`${baseUrl}/${document.id}`);
    expect(req.request.method).toBe('GET');
    req.flush(document);

    expect(result).toEqual(document);
  });
});
