import { HttpClient, HttpEvent, HttpEventType } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, filter, map } from 'rxjs';

import { environment } from '../../../environments/environment';
import { CaseDocument, DocumentType } from '../models/document.model';

/** Progress of an in-flight upload, ending with the stored document. */
export type DocumentUploadEvent =
  { kind: 'progress'; percent: number } | { kind: 'complete'; document: CaseDocument };

@Injectable({ providedIn: 'root' })
export class DocumentService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/cases`;

  /**
   * Uploads a file to the case as multipart/form-data, emitting progress
   * percentages while the request body is sent and a final complete event
   * with the created document.
   */
  uploadDocument(
    caseId: string,
    file: File,
    documentType: DocumentType,
  ): Observable<DocumentUploadEvent> {
    const form = new FormData();
    form.append('file', file, file.name);
    form.append('documentType', documentType);

    return this.http
      .post<CaseDocument>(`${this.baseUrl}/${caseId}/documents`, form, {
        observe: 'events',
        reportProgress: true,
      })
      .pipe(
        map((event) => this.toUploadEvent(event)),
        filter((event): event is DocumentUploadEvent => event !== null),
      );
  }

  getDocuments(caseId: string): Observable<CaseDocument[]> {
    return this.http.get<CaseDocument[]>(`${this.baseUrl}/${caseId}/documents`);
  }

  getDocument(caseId: string, documentId: string): Observable<CaseDocument> {
    return this.http.get<CaseDocument>(`${this.baseUrl}/${caseId}/documents/${documentId}`);
  }

  private toUploadEvent(event: HttpEvent<CaseDocument>): DocumentUploadEvent | null {
    switch (event.type) {
      case HttpEventType.UploadProgress:
        return {
          kind: 'progress',
          percent: event.total ? Math.round((event.loaded / event.total) * 100) : 0,
        };
      case HttpEventType.Response:
        return { kind: 'complete', document: event.body as CaseDocument };
      default:
        return null;
    }
  }
}
