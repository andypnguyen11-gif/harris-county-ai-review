export type KnowledgeDocumentIngestionStatus =
  | 'Uploaded'
  | 'Processing'
  | 'Ingested'
  | 'Failed'
  | 'Deactivated';

export interface KnowledgeDocument {
  id: string;
  title: string;
  fileName: string;
  blobPath: string;
  department: string;
  documentType: string;
  permitType: string;
  version: string | null;
  effectiveDate: string | null;
  sourceUrl: string | null;
  ingestionStatus: KnowledgeDocumentIngestionStatus;
  ingestionDate: string | null;
  createdAt: string;
  updatedAt: string;
}

/**
 * Multipart upload payload. Field names match the API's
 * UploadKnowledgeDocumentRequest form binding; effectiveDate is an
 * ISO 8601 date string (yyyy-MM-dd).
 */
export interface UploadKnowledgeDocumentRequest {
  file: File;
  title: string;
  department: string;
  documentType: string;
  permitType: string;
  version?: string;
  effectiveDate?: string;
  sourceUrl?: string;
}

export const KNOWLEDGE_DOCUMENT_INGESTION_STATUSES: readonly KnowledgeDocumentIngestionStatus[] = [
  'Uploaded',
  'Processing',
  'Ingested',
  'Failed',
  'Deactivated',
];

export const KNOWLEDGE_DOCUMENT_INGESTION_STATUS_LABELS: Record<
  KnowledgeDocumentIngestionStatus,
  string
> = {
  Uploaded: 'Uploaded',
  Processing: 'Processing',
  Ingested: 'Ingested',
  Failed: 'Failed',
  Deactivated: 'Deactivated',
};
