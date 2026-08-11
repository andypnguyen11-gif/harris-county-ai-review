import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { MAX_QUESTION_LENGTH, QuestionResponse } from '../../core/models/question-answer.model';
import { QuestionAnsweringService } from '../../core/services/question-answering.service';

/**
 * Lets reviewers ask natural-language questions of the Harris County
 * reference corpus. Answers are grounded: an answered question lists the
 * corpus sources it cites, and when the corpus lacks evidence the page shows
 * an explicit insufficient-evidence state instead of an answer.
 */
@Component({
  selector: 'app-question-answering',
  imports: [ReactiveFormsModule],
  templateUrl: './question-answering.html',
  styleUrl: './question-answering.scss',
})
export class QuestionAnswering {
  private readonly formBuilder = inject(NonNullableFormBuilder);
  private readonly questionAnsweringService = inject(QuestionAnsweringService);

  protected readonly maxQuestionLength = MAX_QUESTION_LENGTH;

  protected readonly asking = signal(false);
  protected readonly askError = signal(false);
  protected readonly response = signal<QuestionResponse | null>(null);
  /** The question the current response answers, kept for display after the box is edited. */
  protected readonly askedQuestion = signal('');

  protected readonly form = this.formBuilder.group({
    question: ['', [Validators.required, Validators.maxLength(MAX_QUESTION_LENGTH)]],
  });

  protected showQuestionError(): boolean {
    const control = this.form.controls.question;
    return control.invalid && (control.touched || control.dirty);
  }

  protected onSubmit(): void {
    const question = this.form.controls.question.value.trim();
    if (this.form.invalid || question.length === 0) {
      this.form.markAllAsTouched();
      return;
    }

    this.asking.set(true);
    this.askError.set(false);
    this.response.set(null);
    this.askedQuestion.set(question);

    this.questionAnsweringService.ask(question).subscribe({
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
}
