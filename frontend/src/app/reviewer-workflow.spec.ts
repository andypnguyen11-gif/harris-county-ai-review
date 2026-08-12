import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { environment } from '../environments/environment';
import { AuthService } from './core/auth/auth.service';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { toCitationTarget } from './core/models/citation-target.model';
import { CaseDocument } from './core/models/document.model';
import { QuestionResponse } from './core/models/question-answer.model';
import { ValidationReport } from './core/models/validation.model';
import { Case } from './core/models/case.model';
import { CaseService } from './core/services/case.service';
import { DocumentService } from './core/services/document.service';
import { QuestionAnsweringService } from './core/services/question-answering.service';
import { ValidationService } from './core/services/validation.service';
import { makeSession, clearSessionStorage } from './testing/auth-fixtures';
import { makeCase } from './testing/case-fixtures';
import { makeDocument, makePdfFile } from './testing/document-fixtures';
import { makeCitation, makeQuestionResponse } from './testing/question-answer-fixtures';
import { makeValidationItem, makeValidationReport } from './testing/validation-fixtures';

/**
 * The reviewer's whole journey through the client, in the order the MVP test
 * plan describes it: sign in, create a case, upload a document, validate it,
 * open a cited page, and ask questions of both corpora.
 *
 * This is a client-side journey, not a browser end-to-end test — the project
 * has no browser harness, and the backend suite under
 * `HarrisCountyAI.IntegrationTests/EndToEnd` covers the server side against a
 * real database and blob store. What this adds is the contract between them:
 * every request the journey makes is asserted for method, URL, body shape, and
 * bearer token, so a change to either side that breaks the pairing fails here.
 */
describe('reviewer workflow', () => {
  const api = environment.apiUrl;
  let httpMock: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    clearSessionStorage();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => {
    httpMock.verify();
    clearSessionStorage();
  });

  /** Signs in through the API the way the sign-in screen does. */
  function signIn(): void {
    auth.signIn('dev.reviewer').subscribe();
    const request = httpMock.expectOne(`${api}/auth/dev-token`);
    expect(request.request.method).toBe('POST');
    // The request that produces the token cannot carry it.
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush(makeSession());
  }

  it('carries the reviewer from sign-in through validation to a cited document', () => {
    signIn();
    const token = makeSession().accessToken;

    // 1. Create the case.
    const created = makeCase({ name: 'Cypresswood Residence' });
    let caseResult: Case | undefined;
    TestBed.inject(CaseService)
      .createCase({ name: 'Cypresswood Residence', workflowType: 'FloodplainDevelopmentPermit' })
      .subscribe((value) => (caseResult = value));

    const createRequest = httpMock.expectOne(`${api}/cases`);
    expect(createRequest.request.method).toBe('POST');
    expect(createRequest.request.headers.get('Authorization')).toBe(`Bearer ${token}`);
    expect(createRequest.request.body).toEqual({
      name: 'Cypresswood Residence',
      workflowType: 'FloodplainDevelopmentPermit',
    });
    createRequest.flush(created);
    expect(caseResult).toEqual(created);

    // 2. Upload the permit application as multipart form data.
    const stored = makeDocument({
      caseId: created.id,
      fileName: 'permit-application.pdf',
      documentType: 'PermitApplication',
    });
    let uploaded: CaseDocument | undefined;
    TestBed.inject(DocumentService)
      .uploadDocument(created.id, makePdfFile('permit-application.pdf'), 'PermitApplication')
      .subscribe((event) => {
        if (event.kind === 'complete') {
          uploaded = event.document;
        }
      });

    const uploadRequest = httpMock.expectOne(`${api}/cases/${created.id}/documents`);
    expect(uploadRequest.request.method).toBe('POST');
    const form = uploadRequest.request.body as FormData;
    expect((form.get('file') as File).name).toBe('permit-application.pdf');
    expect(form.get('documentType')).toBe('PermitApplication');
    uploadRequest.flush(stored);
    expect(uploaded).toEqual(stored);

    // 3. Run validation and read the report.
    const report = makeValidationReport({
      caseId: created.id,
      items: [
        makeValidationItem({
          requirement: 'Owner name',
          status: 'Complete',
          extractedValue: 'Jane P. Smith',
          documentId: stored.id,
          documentType: 'PermitApplication',
          pageNumber: 1,
        }),
        makeValidationItem({ requirement: 'Site plan', status: 'Missing' }),
      ],
    });
    let reportResult: ValidationReport | undefined;
    TestBed.inject(ValidationService)
      .runValidation(created.id)
      .subscribe((value) => (reportResult = value));

    const validationRequest = httpMock.expectOne(`${api}/cases/${created.id}/validation`);
    expect(validationRequest.request.method).toBe('POST');
    validationRequest.flush(report);
    expect(reportResult?.items.map((item) => item.status)).toEqual(['Complete', 'Missing']);

    // 4. Open the document a report item points at, at the page it names.
    const cited = reportResult!.items[0];
    expect(cited.documentId).toBe(stored.id);
    expect(cited.pageNumber).toBe(1);

    let content: Blob | undefined;
    TestBed.inject(DocumentService)
      .getDocumentContent(created.id, cited.documentId!)
      .subscribe((value) => (content = value));

    const contentRequest = httpMock.expectOne(
      `${api}/cases/${created.id}/documents/${cited.documentId}/content`,
    );
    expect(contentRequest.request.method).toBe('GET');
    expect(contentRequest.request.responseType).toBe('blob');
    contentRequest.flush(new Blob(['%PDF-1.4'], { type: 'application/pdf' }));
    expect(content?.type).toBe('application/pdf');
  });

  it('asks the county corpus and the case documents through the same endpoint', () => {
    signIn();
    const caseId = makeCase().id;
    const service = TestBed.inject(QuestionAnsweringService);

    // A county question defaults to the reference corpus and sends no case id.
    let countyAnswer: QuestionResponse | undefined;
    service
      .ask('What must a development permit application include?')
      .subscribe((value) => (countyAnswer = value));

    const countyRequest = httpMock.expectOne(`${api}/questions`);
    expect(countyRequest.request.body).toEqual({
      question: 'What must a development permit application include?',
    });
    countyRequest.flush(makeQuestionResponse());
    expect(countyAnswer?.outcome).toBe('Answered');
    expect(countyAnswer?.citations[0].source).toBe('County');

    // A case question carries the scope and the case it is about.
    const caseCitation = makeCitation({
      source: 'Case',
      title: 'permit-application.pdf',
      section: null,
      page: 2,
      sourceUrl: null,
    });
    let caseAnswer: QuestionResponse | undefined;
    service
      .ask('Who signed this application?', { scope: 'Case', caseId })
      .subscribe((value) => (caseAnswer = value));

    const caseRequest = httpMock.expectOne(`${api}/questions`);
    expect(caseRequest.request.body).toEqual({
      question: 'Who signed this application?',
      scope: 'Case',
      caseId,
    });
    caseRequest.flush(makeQuestionResponse({ citations: [caseCitation] }));

    // A case citation opens inside its own case; a county citation never does.
    const caseTarget = toCitationTarget(caseAnswer!.citations[0], caseId);
    expect(caseTarget).toMatchObject({ source: 'Case', caseId, page: 2 });
    expect(toCitationTarget(countyAnswer!.citations[0], caseId).caseId).toBeNull();
  });

  it('reports insufficient evidence as an answer rather than an error', () => {
    signIn();

    let answer: QuestionResponse | undefined;
    TestBed.inject(QuestionAnsweringService)
      .ask('What are the permit fees?')
      .subscribe((value) => (answer = value));

    httpMock.expectOne(`${api}/questions`).flush(
      makeQuestionResponse({
        outcome: 'InsufficientEvidence',
        answer: 'The Harris County reference corpus does not contain enough information.',
        citations: [],
      }),
    );

    expect(answer?.outcome).toBe('InsufficientEvidence');
    expect(answer?.citations).toEqual([]);
    // Still signed in: an unanswerable question is not an authentication problem.
    expect(auth.isAuthenticated()).toBe(true);
  });

  it('signs the reviewer out when the API rejects the token mid-journey', () => {
    signIn();
    expect(auth.isAuthenticated()).toBe(true);

    // The interceptor sends the reviewer back to sign-in; the journey is about
    // the session ending, not about the route, so the navigation is stubbed.
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    let failed = false;
    TestBed.inject(CaseService)
      .getCases()
      .subscribe({ error: () => (failed = true) });

    httpMock
      .expectOne(`${api}/cases`)
      .flush({ title: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    expect(failed).toBe(true);
    expect(auth.isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/sign-in']);
  });
});
