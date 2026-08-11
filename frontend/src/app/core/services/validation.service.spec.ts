import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../environments/environment';
import { ValidationReport } from '../models/validation.model';
import { makeValidationReport } from '../../testing/validation-fixtures';
import { ValidationService } from './validation.service';

describe('ValidationService', () => {
  const caseId = '00000000-0000-0000-0000-000000000123';
  const baseUrl = `${environment.apiUrl}/cases/${caseId}/validation`;
  let service: ValidationService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ValidationService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('runValidation POSTs to /cases/{caseId}/validation and returns the created report', () => {
    const report = makeValidationReport({ caseId });
    let result: ValidationReport | undefined;

    service.runValidation(caseId).subscribe((value) => (result = value));

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('POST');
    req.flush(report, { status: 201, statusText: 'Created' });

    expect(result).toEqual(report);
  });

  it('getLatestReport issues GET /cases/{caseId}/validation and returns the report', () => {
    const report = makeValidationReport({ caseId });
    let result: ValidationReport | undefined;

    service.getLatestReport(caseId).subscribe((value) => (result = value));

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('GET');
    req.flush(report);

    expect(result).toEqual(report);
  });

  it('getLatestReport propagates a 404 when the case has never been validated', () => {
    let error: HttpErrorResponse | undefined;

    service.getLatestReport(caseId).subscribe({
      next: () => {
        throw new Error('expected an error, not a value');
      },
      error: (err: HttpErrorResponse) => (error = err),
    });

    httpMock.expectOne(baseUrl).flush(
      { title: 'Not Found', status: 404 },
      { status: 404, statusText: 'Not Found' },
    );

    expect(error).toBeInstanceOf(HttpErrorResponse);
    expect(error?.status).toBe(404);
  });

  it('getReport issues GET /cases/{caseId}/validation/{reportId} and returns the report', () => {
    const report = makeValidationReport({ caseId });
    let result: ValidationReport | undefined;

    service.getReport(caseId, report.id).subscribe((value) => (result = value));

    const req = httpMock.expectOne(`${baseUrl}/${report.id}`);
    expect(req.request.method).toBe('GET');
    req.flush(report);

    expect(result).toEqual(report);
  });
});
