import { Citation, CitationSource } from './question-answer.model';

/**
 * Everything the document viewer needs to open a cited source at the right
 * place. Derived from a citation (or a validation report item) rather than
 * passed around as one, so the viewer has a single input shape whichever
 * screen opened it.
 */
export interface CitationTarget {
  /** Which corpus the source belongs to; decides how it is opened and labelled. */
  source: CitationSource;
  /**
   * The case the document belongs to. Required to fetch a Case document's
   * file; always null for County sources, which live in the reference corpus
   * rather than on a case.
   */
  caseId: string | null;
  documentId: string;
  title: string;
  section: string | null;
  /** 1-based page the viewer should open at, when the source names one. */
  page: number | null;
  /** Public URL of the county document, when one exists. */
  sourceUrl: string | null;
}

/**
 * Builds a viewer target from a citation. A Case citation needs the case whose
 * documents were searched — without it the file cannot be fetched, and the
 * viewer says so rather than guessing.
 */
export function toCitationTarget(citation: Citation, caseId: string | null): CitationTarget {
  return {
    source: citation.source,
    caseId: citation.source === 'Case' ? caseId : null,
    documentId: citation.documentId,
    title: citation.title,
    section: citation.section,
    page: citation.page,
    sourceUrl: citation.sourceUrl,
  };
}
