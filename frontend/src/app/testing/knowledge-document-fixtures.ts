import { KnowledgeDocument } from '../core/models/knowledge-document.model';

let sequence = 0;

export function makeKnowledgeDocument(
  overrides: Partial<KnowledgeDocument> = {},
): KnowledgeDocument {
  sequence += 1;
  return {
    id: `00000000-0000-0000-0001-${String(sequence).padStart(12, '0')}`,
    title: `Reference Document ${sequence}`,
    fileName: `reference-${sequence}.pdf`,
    blobPath: `knowledge-base/reference-${sequence}.pdf`,
    department: 'Harris County Engineering',
    documentType: 'Regulation',
    permitType: 'FloodplainDevelopmentPermit',
    version: null,
    effectiveDate: null,
    sourceUrl: null,
    ingestionStatus: 'Uploaded',
    ingestionDate: null,
    createdAt: '2026-08-01T10:00:00Z',
    updatedAt: '2026-08-01T10:00:00Z',
    ...overrides,
  };
}
