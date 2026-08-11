import { Case } from '../core/models/case.model';

let sequence = 0;

export function makeCase(overrides: Partial<Case> = {}): Case {
  sequence += 1;
  return {
    id: `00000000-0000-0000-0000-${String(sequence).padStart(12, '0')}`,
    caseNumber: `HC-2026-${String(sequence).padStart(4, '0')}`,
    name: `Test Case ${sequence}`,
    workflowType: 'FloodplainDevelopmentPermit',
    status: 'New',
    createdAt: '2026-08-01T10:00:00Z',
    updatedAt: '2026-08-01T10:00:00Z',
    ...overrides,
  };
}
