import { ValidationReport, ValidationReportItem } from '../core/models/validation.model';

let sequence = 0;

export function makeValidationItem(
  overrides: Partial<ValidationReportItem> = {},
): ValidationReportItem {
  sequence += 1;
  return {
    id: `20000000-0000-0000-0000-${String(sequence).padStart(12, '0')}`,
    ruleName: `RequiredFieldRule(Requirement ${sequence})`,
    requirement: `Requirement ${sequence}`,
    validationType: 'Deterministic',
    status: 'Complete',
    message: 'The requirement is satisfied.',
    extractedValue: null,
    documentId: null,
    documentType: null,
    pageNumber: null,
    boundingBox: null,
    ...overrides,
  };
}

export function makeValidationReport(overrides: Partial<ValidationReport> = {}): ValidationReport {
  sequence += 1;
  return {
    id: `30000000-0000-0000-0000-${String(sequence).padStart(12, '0')}`,
    caseId: `00000000-0000-0000-0000-${String(sequence).padStart(12, '0')}`,
    workflowType: 'FloodplainDevelopmentPermit',
    createdAt: '2026-08-11T15:30:00Z',
    items: [makeValidationItem()],
    ...overrides,
  };
}
