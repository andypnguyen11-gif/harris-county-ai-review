import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../environments/environment';
import { makeQuestionResponse } from '../../testing/question-answer-fixtures';
import { QuestionAnsweringService } from './question-answering.service';

describe('QuestionAnsweringService scoped questions', () => {
  const baseUrl = `${environment.apiUrl}/questions`;
  let service: QuestionAnsweringService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(QuestionAnsweringService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('sends the scope and case id for case-scoped questions', () => {
    service
      .ask('Who signed this application?', { scope: 'Case', caseId: 'case-42' })
      .subscribe();

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.body).toEqual({
      question: 'Who signed this application?',
      scope: 'Case',
      caseId: 'case-42',
    });
    req.flush(makeQuestionResponse());
  });

  it('omits scope and case id entirely when not provided', () => {
    service.ask('What does the county require?').subscribe();

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.body).toEqual({ question: 'What does the county require?' });
    req.flush(makeQuestionResponse());
  });
});
