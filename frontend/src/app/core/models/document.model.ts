export type DocumentType =
  | 'PermitApplication'
  | 'SitePlan'
  | 'ElevationCertificate'
  | 'DrainagePlan'
  | 'Affidavit'
  | 'SupportingDocument'
  | 'Other';

export type DocumentProcessingStatus =
  'Pending' | 'Uploaded' | 'Extracting' | 'Extracted' | 'Normalized' | 'Failed';

/** Mirrors the API's DocumentDto. */
export interface CaseDocument {
  id: string;
  caseId: string;
  fileName: string;
  documentType: DocumentType;
  processingStatus: DocumentProcessingStatus;
  createdAt: string;
}

/**
 * Mirrors the API's ProcessDocumentResult. The document's own
 * processingStatus is the single source of truth for how the run ended:
 * `Normalized` when it completed, `Failed` when it did not — in which case
 * `failureReason` explains why. The API answers 200 for both, so a failed run
 * is distinguishable from a request that never reached the server.
 */
export interface DocumentProcessingResult {
  document: CaseDocument;
  failureReason: string | null;
}

export const DOCUMENT_TYPES: readonly DocumentType[] = [
  'PermitApplication',
  'SitePlan',
  'ElevationCertificate',
  'DrainagePlan',
  'Affidavit',
  'SupportingDocument',
  'Other',
];

export const DEFAULT_DOCUMENT_TYPE: DocumentType = 'SupportingDocument';

export const DOCUMENT_TYPE_LABELS: Record<DocumentType, string> = {
  PermitApplication: 'Permit Application',
  SitePlan: 'Site Plan',
  ElevationCertificate: 'Elevation Certificate',
  DrainagePlan: 'Drainage Plan',
  Affidavit: 'Affidavit',
  SupportingDocument: 'Supporting Document',
  Other: 'Other',
};

export const DOCUMENT_PROCESSING_STATUS_LABELS: Record<DocumentProcessingStatus, string> = {
  Pending: 'Pending',
  Uploaded: 'Uploaded',
  Extracting: 'Extracting',
  Extracted: 'Extracted',
  Normalized: 'Normalized',
  Failed: 'Failed',
};
