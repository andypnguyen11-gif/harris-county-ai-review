import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../environments/environment';
import { QuestionResponse } from '../models/question-answer.model';
import { makeQuestionResponse } from '../../testing/question-answer-fixtures';
import { QuestionAnsweringService } from './question-answering.service';

describe('QuestionAnsweringService', () => {
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

  it('ask posts the question to /questions and returns the response', () => {
    const expected = makeQuestionResponse();
    let result: QuestionResponse | undefined;

    service
      .ask('What must a floodplain permit application include?')
      .subscribe((value) => (result = value));

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      question: 'What must a floodplain permit application include?',
    });
    req.flush(expected);

    expect(result).toEqual(expected);
  });

  it('propagates HTTP errors to the subscriber', () => {
    let error: HttpErrorResponse | undefined;

    service.ask('What are the fees?').subscribe({
      next: () => {
        throw new Error('expected an error, not a value');
      },
      error: (err: HttpErrorResponse) => (error = err),
    });

    httpMock
      .expectOne(baseUrl)
      .flush(
        { title: 'The question could not be answered.' },
        { status: 502, statusText: 'Bad Gateway' },
      );

    expect(error).toBeInstanceOf(HttpErrorResponse);
    expect(error?.status).toBe(502);
  });
});
