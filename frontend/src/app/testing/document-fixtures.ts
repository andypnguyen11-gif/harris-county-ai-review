import { CaseDocument } from '../core/models/document.model';

let sequence = 0;

export function makeDocument(overrides: Partial<CaseDocument> = {}): CaseDocument {
  sequence += 1;
  return {
    id: `10000000-0000-0000-0000-${String(sequence).padStart(12, '0')}`,
    caseId: `00000000-0000-0000-0000-${String(sequence).padStart(12, '0')}`,
    fileName: `document-${sequence}.pdf`,
    documentType: 'SupportingDocument',
    processingStatus: 'Uploaded',
    createdAt: '2026-08-01T12:00:00Z',
    ...overrides,
  };
}

export function makePdfFile(name = 'site-plan.pdf', content = '%PDF-1.4 test'): File {
  return new File([content], name, { type: 'application/pdf' });
}
