import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  KnowledgeDocument,
  UploadKnowledgeDocumentRequest,
} from '../models/knowledge-document.model';

@Injectable({ providedIn: 'root' })
export class KnowledgeBaseService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/knowledge-base/documents`;

  getDocuments(includeDeactivated = false): Observable<KnowledgeDocument[]> {
    const params = new HttpParams().set('includeDeactivated', includeDeactivated);
    return this.http.get<KnowledgeDocument[]>(this.baseUrl, { params });
  }

  uploadDocument(request: UploadKnowledgeDocumentRequest): Observable<KnowledgeDocument> {
    const formData = new FormData();
    formData.append('file', request.file, request.file.name);
    formData.append('title', request.title);
    formData.append('department', request.department);
    formData.append('documentType', request.documentType);
    formData.append('permitType', request.permitType);
    if (request.version) {
      formData.append('version', request.version);
    }
    if (request.effectiveDate) {
      formData.append('effectiveDate', request.effectiveDate);
    }
    if (request.sourceUrl) {
      formData.append('sourceUrl', request.sourceUrl);
    }
    return this.http.post<KnowledgeDocument>(this.baseUrl, formData);
  }

  deactivateDocument(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
