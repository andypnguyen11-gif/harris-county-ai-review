import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { QuestionResponse } from '../models/question-answer.model';

@Injectable({ providedIn: 'root' })
export class QuestionAnsweringService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/questions`;

  /**
   * Asks a question of the Harris County reference corpus. The API answers
   * with citations, or reports insufficient evidence; technical failures
   * surface as HTTP errors.
   */
  ask(question: string): Observable<QuestionResponse> {
    return this.http.post<QuestionResponse>(this.baseUrl, { question });
  }
}
