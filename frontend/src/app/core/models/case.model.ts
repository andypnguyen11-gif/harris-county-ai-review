export type WorkflowType = 'FloodplainDevelopmentPermit';

export type CaseStatus =
  | 'New'
  | 'Processing'
  | 'ReadyForReview'
  | 'InReview'
  | 'Completed';

export interface Case {
  id: string;
  caseNumber: string;
  name: string;
  workflowType: WorkflowType;
  status: CaseStatus;
  createdAt: string;
  updatedAt: string;
}

export interface CreateCaseRequest {
  name: string;
  workflowType: WorkflowType;
}

export interface UpdateCaseRequest {
  name?: string;
  status?: CaseStatus;
}

export const CASE_STATUSES: readonly CaseStatus[] = [
  'New',
  'Processing',
  'ReadyForReview',
  'InReview',
  'Completed',
];

export const CASE_STATUS_LABELS: Record<CaseStatus, string> = {
  New: 'New',
  Processing: 'Processing',
  ReadyForReview: 'Ready for Review',
  InReview: 'In Review',
  Completed: 'Completed',
};

export const WORKFLOW_TYPE_LABELS: Record<WorkflowType, string> = {
  FloodplainDevelopmentPermit: 'Floodplain Development Permit',
};
