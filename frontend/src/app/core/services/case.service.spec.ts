import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../environments/environment';
import { Case } from '../models/case.model';
import { makeCase } from '../../testing/case-fixtures';
import { CaseService } from './case.service';

describe('CaseService', () => {
  const baseUrl = `${environment.apiUrl}/cases`;
  let service: CaseService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CaseService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('getCases issues GET /cases and returns the case list', () => {
    const cases = [makeCase(), makeCase({ status: 'Completed' })];
    let result: Case[] | undefined;

    service.getCases().subscribe((value) => (result = value));

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('GET');
    req.flush(cases);

    expect(result).toEqual(cases);
  });

  it('getCase issues GET /cases/{id} and returns the case', () => {
    const caseItem = makeCase();
    let result: Case | undefined;

    service.getCase(caseItem.id).subscribe((value) => (result = value));

    const req = httpMock.expectOne(`${baseUrl}/${caseItem.id}`);
    expect(req.request.method).toBe('GET');
    req.flush(caseItem);

    expect(result).toEqual(caseItem);
  });

  it('createCase issues POST /cases with the request body and returns the created case', () => {
    const created = makeCase({ name: 'New Development Permit' });
    let result: Case | undefined;

    service
      .createCase({ name: 'New Development Permit', workflowType: 'FloodplainDevelopmentPermit' })
      .subscribe((value) => (result = value));

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      name: 'New Development Permit',
      workflowType: 'FloodplainDevelopmentPermit',
    });
    req.flush(created);

    expect(result).toEqual(created);
  });

  it('updateCase issues PATCH /cases/{id} with the request body and returns the updated case', () => {
    const updated = makeCase({ status: 'InReview' });
    let result: Case | undefined;

    service.updateCase(updated.id, { status: 'InReview' }).subscribe((value) => (result = value));

    const req = httpMock.expectOne(`${baseUrl}/${updated.id}`);
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ status: 'InReview' });
    req.flush(updated);

    expect(result).toEqual(updated);
  });

  it('propagates HTTP errors to the subscriber', () => {
    let error: HttpErrorResponse | undefined;

    service.getCases().subscribe({
      next: () => {
        throw new Error('expected an error, not a value');
      },
      error: (err: HttpErrorResponse) => (error = err),
    });

    httpMock
      .expectOne(baseUrl)
      .flush({ message: 'boom' }, { status: 500, statusText: 'Internal Server Error' });

    expect(error).toBeInstanceOf(HttpErrorResponse);
    expect(error?.status).toBe(500);
  });
});
