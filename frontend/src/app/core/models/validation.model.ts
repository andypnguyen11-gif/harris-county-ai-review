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

/**
 * A region of a document page, as fractions of the page's width and height
 * with the origin at the top-left. Mirrors the API's BoundingBox. Fractions
 * rather than pixels so the same values place a box correctly at any zoom or
 * canvas size — multiply by the rendered dimensions and draw.
 */
export interface BoundingBox {
  pageNumber: number;
  x: number;
  y: number;
  width: number;
  height: number;
}

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
  /** Where on the page the finding came from; null when it could not be located. */
  boundingBox: BoundingBox | null;
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
