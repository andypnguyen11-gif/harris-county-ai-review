import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { QuestionResponse, QuestionScope } from '../models/question-answer.model';

/** Optional scoping of a question to a single case's uploaded documents. */
export interface AskOptions {
  scope?: QuestionScope;
  /** Required by the API when scope is 'Case'. */
  caseId?: string;
}

@Injectable({ providedIn: 'root' })
export class QuestionAnsweringService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/questions`;

  /**
   * Asks a question of the Harris County reference corpus (the default) or,
   * scoped by case id, of one case's uploaded documents. The API answers
   * with citations, or reports insufficient evidence; technical failures
   * surface as HTTP errors.
   */
  ask(question: string, options?: AskOptions): Observable<QuestionResponse> {
    return this.http.post<QuestionResponse>(this.baseUrl, {
      question,
      ...(options?.scope ? { scope: options.scope } : {}),
      ...(options?.caseId ? { caseId: options.caseId } : {}),
    });
  }
}
