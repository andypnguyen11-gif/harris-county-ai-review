import { Component, computed, input, output } from '@angular/core';

import { CitationTarget, toCitationTarget } from '../../../core/models/citation-target.model';
import {
  CITATION_SOURCE_LABELS,
  Citation as CitationModel,
} from '../../../core/models/question-answer.model';

/**
 * One cited source, rendered so a reviewer can both read where it came from
 * and go look at it.
 *
 * The source badge is the important part: a county citation states what
 * Harris County requires, a case citation states only what this applicant
 * submitted, and an answer that mixes the two is only checkable if the
 * distinction survives to the screen. The two are given different labels and
 * different styling for that reason.
 */
@Component({
  selector: 'app-citation',
  templateUrl: './citation.html',
  styleUrl: './citation.scss',
  host: { class: 'citation' },
})
export class Citation {
  readonly citation = input.required<CitationModel>();

  /** The case whose documents were searched; needed to open a Case citation's file. */
  readonly caseId = input<string | null>(null);

  /** Emitted when the reviewer asks to see the cited source. */
  readonly view = output<CitationTarget>();

  protected readonly sourceLabels = CITATION_SOURCE_LABELS;

  protected readonly sourceClass = computed(
    () => `citation__source citation__source--${this.citation().source.toLowerCase()}`,
  );

  /** Label for the view action, naming the page when the citation points at one. */
  protected readonly viewLabel = computed(() => {
    const page = this.citation().page;
    return page === null ? 'View source' : `View page ${page}`;
  });

  protected onView(): void {
    this.view.emit(toCitationTarget(this.citation(), this.caseId()));
  }
}
