import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../environments/environment';
import { KnowledgeDocument } from '../models/knowledge-document.model';
import { makeKnowledgeDocument } from '../../testing/knowledge-document-fixtures';
import { KnowledgeBaseService } from './knowledge-base.service';

describe('KnowledgeBaseService', () => {
  const baseUrl = `${environment.apiUrl}/knowledge-base/documents`;
  let service: KnowledgeBaseService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(KnowledgeBaseService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('getDocuments issues GET /knowledge-base/documents with includeDeactivated=false by default', () => {
    const documents = [makeKnowledgeDocument(), makeKnowledgeDocument({ ingestionStatus: 'Ingested' })];
    let result: KnowledgeDocument[] | undefined;

    service.getDocuments().subscribe((value) => (result = value));

    const req = httpMock.expectOne(`${baseUrl}?includeDeactivated=false`);
    expect(req.request.method).toBe('GET');
    req.flush(documents);

    expect(result).toEqual(documents);
  });

  it('getDocuments passes includeDeactivated=true when requested', () => {
    service.getDocuments(true).subscribe();

    const req = httpMock.expectOne(`${baseUrl}?includeDeactivated=true`);
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('uploadDocument posts multipart form data with all provided fields', () => {
    const created = makeKnowledgeDocument({ title: 'Floodplain Regulations' });
    const file = new File(['content'], 'regulations.pdf', { type: 'application/pdf' });
    let result: KnowledgeDocument | undefined;

    service
      .uploadDocument({
        file,
        title: 'Floodplain Regulations',
        department: 'Harris County Engineering',
        documentType: 'Regulation',
        permitType: 'FloodplainDevelopmentPermit',
        version: '2024 Edition',
        effectiveDate: '2024-01-15',
        sourceUrl: 'https://www.hcfcd.org/regulations',
      })
      .subscribe((value) => (result = value));

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('POST');
    const body = req.request.body as FormData;
    expect(body).toBeInstanceOf(FormData);
    expect(body.get('file')).toBeInstanceOf(File);
    expect((body.get('file') as File).name).toBe('regulations.pdf');
    expect(body.get('title')).toBe('Floodplain Regulations');
    expect(body.get('department')).toBe('Harris County Engineering');
    expect(body.get('documentType')).toBe('Regulation');
    expect(body.get('permitType')).toBe('FloodplainDevelopmentPermit');
    expect(body.get('version')).toBe('2024 Edition');
    expect(body.get('effectiveDate')).toBe('2024-01-15');
    expect(body.get('sourceUrl')).toBe('https://www.hcfcd.org/regulations');
    req.flush(created);

    expect(result).toEqual(created);
  });

  it('uploadDocument omits optional fields that are not provided', () => {
    const file = new File(['content'], 'guide.pdf', { type: 'application/pdf' });

    service
      .uploadDocument({
        file,
        title: 'Submittal Guide',
        department: 'Permits',
        documentType: 'Guide',
        permitType: 'FloodplainDevelopmentPermit',
      })
      .subscribe();

    const req = httpMock.expectOne(baseUrl);
    const body = req.request.body as FormData;
    expect(body.has('version')).toBe(false);
    expect(body.has('effectiveDate')).toBe(false);
    expect(body.has('sourceUrl')).toBe(false);
    req.flush(makeKnowledgeDocument());
  });

  it('deactivateDocument issues DELETE /knowledge-base/documents/{id}', () => {
    const document = makeKnowledgeDocument();
    let completed = false;

    service.deactivateDocument(document.id).subscribe({ complete: () => (completed = true) });

    const req = httpMock.expectOne(`${baseUrl}/${document.id}`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });

    expect(completed).toBe(true);
  });

  it('propagates HTTP errors to the subscriber', () => {
    let error: HttpErrorResponse | undefined;

    service.getDocuments().subscribe({
      next: () => {
        throw new Error('expected an error, not a value');
      },
      error: (err: HttpErrorResponse) => (error = err),
    });

    httpMock
      .expectOne(`${baseUrl}?includeDeactivated=false`)
      .flush({ message: 'boom' }, { status: 500, statusText: 'Internal Server Error' });

    expect(error).toBeInstanceOf(HttpErrorResponse);
    expect(error?.status).toBe(500);
  });
});
