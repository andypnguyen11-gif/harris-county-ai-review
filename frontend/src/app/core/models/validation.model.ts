import { WorkflowType } from './case.model';
import { DocumentType } from './document.model';

export type ValidationStatus =
  | 'Complete'
  | 'Missing'
  | 'Invalid'
  | 'PotentiallyIncomplete'
  | 'NeedsHumanReview'
  | 'UnableToDetermine';

/** How a validation result was produced: deterministic rule code or AI evaluation. */
export type ValidationType = 'Deterministic' | 'Semantic';

/** Mirrors the API's ValidationReportItemDto. */
export interface ValidationReportItem {
  id: string;
  ruleName: string;
  requirement: string;
  validationType: ValidationType;
  status: ValidationStatus;
  message: string;
  extractedValue: string | null;
  documentId: string | null;
  documentType: DocumentType | null;
  pageNumber: number | null;
}

/** Mirrors the API's ValidationReportDto. */
export interface ValidationReport {
  id: string;
  caseId: string;
  workflowType: WorkflowType;
  createdAt: string;
  items: ValidationReportItem[];
}

export const VALIDATION_STATUS_LABELS: Record<ValidationStatus, string> = {
  Complete: 'Complete',
  Missing: 'Missing',
  Invalid: 'Invalid',
  PotentiallyIncomplete: 'Potentially Incomplete',
  NeedsHumanReview: 'Needs Human Review',
  UnableToDetermine: 'Unable to Determine',
};

export const VALIDATION_TYPE_LABELS: Record<ValidationType, string> = {
  Deterministic: 'Deterministic',
  Semantic: 'AI evaluation',
};
