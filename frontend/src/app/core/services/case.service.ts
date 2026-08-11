import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { Case, CreateCaseRequest, UpdateCaseRequest } from '../models/case.model';

@Injectable({ providedIn: 'root' })
export class CaseService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/cases`;

  getCases(): Observable<Case[]> {
    return this.http.get<Case[]>(this.baseUrl);
  }

  getCase(id: string): Observable<Case> {
    return this.http.get<Case>(`${this.baseUrl}/${id}`);
  }

  createCase(request: CreateCaseRequest): Observable<Case> {
    return this.http.post<Case>(this.baseUrl, request);
  }

  updateCase(id: string, request: UpdateCaseRequest): Observable<Case> {
    return this.http.patch<Case>(`${this.baseUrl}/${id}`, request);
  }
}
