import { Citation, QuestionResponse } from '../core/models/question-answer.model';

let sequence = 0;

export function makeCitation(overrides: Partial<Citation> = {}): Citation {
  sequence += 1;
  return {
    number: 1,
    source: 'County',
    chunkId: `chunk-${sequence}`,
    documentId: `40000000-0000-0000-0000-${String(sequence).padStart(12, '0')}`,
    title: 'Floodplain Regulations',
    section: 'Section 4.2',
    page: 17,
    sourceUrl: 'https://www.hcfcd.org/regulations',
    ...overrides,
  };
}

export function makeQuestionResponse(overrides: Partial<QuestionResponse> = {}): QuestionResponse {
  return {
    outcome: 'Answered',
    answer: 'A completed application form is required.',
    citations: [makeCitation()],
    promptVersion: 'corpus-qa/v1',
    modelDeployment: 'test-deployment',
    ...overrides,
  };
}
