/** Longest accepted question, mirroring the API's limit. */
export const MAX_QUESTION_LENGTH = 1000;

/** How a question-answering attempt concluded. Mirrors the API's QuestionAnswerOutcome. */
export type QuestionAnswerOutcome = 'Answered' | 'InsufficientEvidence' | 'Failed';

/** Mirrors the API's Citation: one corpus source the answer is grounded in. */
export interface Citation {
  /** The source number the answer cites (1-based). */
  number: number;
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
