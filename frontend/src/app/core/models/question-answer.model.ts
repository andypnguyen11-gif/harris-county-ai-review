/** Longest accepted question, mirroring the API's limit. */
export const MAX_QUESTION_LENGTH = 1000;

/**
 * What a question is asked of: the Harris County reference corpus, or the
 * documents uploaded to one specific case. Mirrors the API's QuestionScope.
 */
export type QuestionScope = 'County' | 'Case';

export const QUESTION_SCOPE_LABELS: Record<QuestionScope, string> = {
  County: 'County requirements',
  Case: 'Case documents',
};

/** How a question-answering attempt concluded. Mirrors the API's QuestionAnswerOutcome. */
export type QuestionAnswerOutcome = 'Answered' | 'InsufficientEvidence' | 'Failed';

/**
 * Which corpus a cited passage came from. Mirrors the API's SourceType, and
 * is the distinction a reviewer must never lose: County text states what
 * Harris County requires, Case text states only what the applicant submitted.
 */
export type CitationSource = 'County' | 'Case';

export const CITATION_SOURCE_LABELS: Record<CitationSource, string> = {
  County: 'County requirement',
  Case: 'Applicant submission',
};

/** Mirrors the API's Citation: one corpus source the answer is grounded in. */
export interface Citation {
  /** The source number the answer cites (1-based). */
  number: number;
  /** The corpus the passage was retrieved from. */
  source: CitationSource;
  chunkId: string;
  documentId: string;
  title: string;
  section: string | null;
  page: number | null;
  sourceUrl: string | null;
}

/** Mirrors the API's QuestionResponse. */
export interface QuestionResponse {
  outcome: QuestionAnswerOutcome;
  /** The grounded answer when answered; otherwise an explanation of why no answer is available. */
  answer: string;
  citations: Citation[];
  promptVersion: string;
  modelDeployment: string | null;
}
