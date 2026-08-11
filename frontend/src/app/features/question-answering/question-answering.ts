import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { Case } from '../../core/models/case.model';
import { CitationTarget } from '../../core/models/citation-target.model';
import {
  MAX_QUESTION_LENGTH,
  QUESTION_SCOPE_LABELS,
  QuestionResponse,
  QuestionScope,
} from '../../core/models/question-answer.model';
import { CaseService } from '../../core/services/case.service';
import { QuestionAnsweringService } from '../../core/services/question-answering.service';
import { Citation } from '../../shared/components/citation/citation';
import { DocumentViewer } from '../document-viewer/document-viewer';

/**
 * Lets reviewers ask natural-language questions of the Harris County
 * reference corpus, or — scoped to one case — of the documents an applicant
 * submitted. Answers are grounded: an answered question lists the sources it
 * cites, and when the evidence is lacking the page shows an explicit
 * insufficient-evidence state instead of an answer.
 *
 * Citations are verifiable rather than decorative: each names the corpus it
 * came from and opens the source document in the viewer, at the cited page.
 */
@Component({
  selector: 'app-question-answering',
  imports: [ReactiveFormsModule, Citation, DocumentViewer],
  templateUrl: './question-answering.html',
  styleUrl: './question-answering.scss',
})
export class QuestionAnswering {
  private readonly formBuilder = inject(NonNullableFormBuilder);
  private readonly questionAnsweringService = inject(QuestionAnsweringService);
  private readonly caseService = inject(CaseService);

  protected readonly maxQuestionLength = MAX_QUESTION_LENGTH;
  protected readonly scopeLabels = QUESTION_SCOPE_LABELS;

  protected readonly asking = signal(false);
  protected readonly askError = signal(false);
  protected readonly response = signal<QuestionResponse | null>(null);
  /** The question the current response answers, kept for display after the box is edited. */
  protected readonly askedQuestion = signal('');
  /** The scope the current response was answered under. */
  protected readonly askedScope = signal<QuestionScope>('County');
  /** The case the current response was answered against; needed to open its citations. */
  protected readonly askedCaseId = signal<string | null>(null);
  /** The cited source the viewer is showing, or null when it is closed. */
  protected readonly viewerTarget = signal<CitationTarget | null>(null);

  protected readonly scope = signal<QuestionScope>('County');
  /** Cases available for the Case scope; null until first loaded. */
  protected readonly cases = signal<Case[] | null>(null);
  protected readonly casesLoading = signal(false);
  protected readonly casesError = signal(false);
  protected readonly caseRequiredError = signal(false);

  protected readonly form = this.formBuilder.group({
    question: ['', [Validators.required, Validators.maxLength(MAX_QUESTION_LENGTH)]],
    caseId: [''],
  });

  protected showQuestionError(): boolean {
    const control = this.form.controls.question;
    return control.invalid && (control.touched || control.dirty);
  }

  protected setScope(scope: QuestionScope): void {
    this.scope.set(scope);
    this.caseRequiredError.set(false);
    if (scope === 'Case' && this.cases() === null && !this.casesLoading()) {
      this.loadCases();
    }
  }

  protected loadCases(): void {
    this.casesLoading.set(true);
    this.casesError.set(false);
    this.caseService.getCases().subscribe({
      next: (cases) => {
        this.cases.set(cases);
        this.casesLoading.set(false);
      },
      error: () => {
        this.casesError.set(true);
        this.casesLoading.set(false);
      },
    });
  }

  protected onSubmit(): void {
    const question = this.form.controls.question.value.trim();
    if (this.form.invalid || question.length === 0) {
      this.form.markAllAsTouched();
      return;
    }

    const scope = this.scope();
    const caseId = this.form.controls.caseId.value;
    if (scope === 'Case' && !caseId) {
      this.caseRequiredError.set(true);
      return;
    }

    this.asking.set(true);
    this.askError.set(false);
    this.caseRequiredError.set(false);
    this.response.set(null);
    this.askedQuestion.set(question);
    this.askedScope.set(scope);
    this.askedCaseId.set(scope === 'Case' ? caseId : null);
    // A new answer means new sources; whatever was open no longer belongs.
    this.viewerTarget.set(null);

    const request =
      scope === 'Case'
        ? this.questionAnsweringService.ask(question, { scope: 'Case', caseId })
        : this.questionAnsweringService.ask(question);

    request.subscribe({
      next: (response) => {
        this.response.set(response);
        this.asking.set(false);
      },
      error: () => {
        this.askError.set(true);
        this.asking.set(false);
      },
    });
  }

  protected openSource(target: CitationTarget): void {
    this.viewerTarget.set(target);
  }

  protected closeSource(): void {
    this.viewerTarget.set(null);
  }
}
