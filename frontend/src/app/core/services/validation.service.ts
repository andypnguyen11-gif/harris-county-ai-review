import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { ValidationReport } from '../models/validation.model';

@Injectable({ providedIn: 'root' })
export class ValidationService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/cases`;

  /** Runs the case's workflow rules and returns the newly persisted report. */
  runValidation(caseId: string): Observable<ValidationReport> {
    return this.http.post<ValidationReport>(`${this.baseUrl}/${caseId}/validation`, null);
  }

  /** Returns the case's most recent report; the API responds 404 when the case has never been validated. */
  getLatestReport(caseId: string): Observable<ValidationReport> {
    return this.http.get<ValidationReport>(`${this.baseUrl}/${caseId}/validation`);
  }

  getReport(caseId: string, reportId: string): Observable<ValidationReport> {
    return this.http.get<ValidationReport>(`${this.baseUrl}/${caseId}/validation/${reportId}`);
  }
}
