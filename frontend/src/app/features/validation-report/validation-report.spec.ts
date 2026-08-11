import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { ValidationService } from '../../core/services/validation.service';
import { makeValidationItem, makeValidationReport } from '../../testing/validation-fixtures';
import { ValidationReportPanel } from './validation-report';

function notFound() {
  return throwError(() => new HttpErrorResponse({ status: 404, statusText: 'Not Found' }));
}

function serverError() {
  return throwError(
    () => new HttpErrorResponse({ status: 500, statusText: 'Internal Server Error' }),
  );
}

describe('ValidationReportPanel', () => {
  const caseId = '00000000-0000-0000-0000-000000000123';
  let getLatestReport: ReturnType<typeof vi.fn>;
  let runValidation: ReturnType<typeof vi.fn>;

  function setup(): void {
    getLatestReport ??= vi.fn(() => notFound());
    runValidation ??= vi.fn();
    TestBed.configureTestingModule({
      imports: [ValidationReportPanel],
      providers: [{ provide: ValidationService, useValue: { getLatestReport, runValidation } }],
    });
  }

  afterEach(() => {
    getLatestReport = undefined as unknown as ReturnType<typeof vi.fn>;
    runValidation = undefined as unknown as ReturnType<typeof vi.fn>;
  });

  async function render() {
    setup();
    const fixture = TestBed.createComponent(ValidationReportPanel);
    fixture.componentRef.setInput('caseId', caseId);
    await fixture.whenStable();
    return fixture;
  }

  function element(fixture: Awaited<ReturnType<typeof render>>): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  it('loads the latest report and renders items grouped by document with status and evidence', async () => {
    const report = makeValidationReport({
      caseId,
      createdAt: '2026-08-11T15:30:00Z',
      items: [
        makeValidationItem({
          requirement: 'Development permit application',
          status: 'Complete',
          message: 'A PermitApplication document is present.',
          documentType: 'PermitApplication',
        }),
        makeValidationItem({
          requirement: 'Owner name',
          status: 'Complete',
          message: 'The field is present.',
          extractedValue: 'Jane P. Smith',
          documentType: 'PermitApplication',
          pageNumber: 1,
        }),
        makeValidationItem({
          requirement: 'Site plan',
          status: 'Missing',
          message: 'No SitePlan document was found in the submission package.',
        }),
      ],
    });
    getLatestReport = vi.fn(() => of(report));
    const fixture = await render();
    const el = element(fixture);

    expect(getLatestReport).toHaveBeenCalledWith(caseId);

    const groupTitles = [...el.querySelectorAll('.report-group__title')].map((title) =>
      title.textContent?.trim(),
    );
    expect(groupTitles).toEqual(['Permit Application', 'Submission package']);

    const applicationGroup = el.querySelectorAll('.report-group')[0];
    expect(applicationGroup.querySelectorAll('.report-item')).toHaveLength(2);
    expect(applicationGroup.textContent).toContain('Owner name');
    expect(applicationGroup.textContent).toContain('Jane P. Smith');
    expect(applicationGroup.textContent).toContain('Page 1');

    const packageGroup = el.querySelectorAll('.report-group')[1];
    expect(packageGroup.textContent).toContain('Site plan');
    expect(packageGroup.textContent).toContain('Missing');

    expect(el.textContent).toContain('Last run');
    expect(el.querySelector('.report-summary')?.textContent).toContain('2 complete');
    expect(el.querySelector('.report-summary')?.textContent).toContain('1 issue');
  });

  it('marks each result with how it was produced', async () => {
    getLatestReport = vi.fn(() =>
      of(
        makeValidationReport({
          caseId,
          items: [
            makeValidationItem({ validationType: 'Deterministic' }),
            makeValidationItem({ validationType: 'Semantic' }),
          ],
        }),
      ),
    );
    const fixture = await render();
    const chips = [...element(fixture).querySelectorAll('.validation-type')];

    expect(chips.map((chip) => chip.textContent?.trim())).toEqual([
      'Deterministic',
      'AI evaluation',
    ]);
    expect(chips[1].classList).toContain('validation-type--semantic');
  });

  it('shows an empty state with a run action before the first validation run', async () => {
    getLatestReport = vi.fn(() => notFound());
    const fixture = await render();
    const el = element(fixture);

    expect(el.textContent).toContain('No validation report yet.');
    expect(el.textContent).toContain('This case has not been validated yet.');

    const runButton = [...el.querySelectorAll('button')].find(
      (button) => button.textContent?.trim() === 'Run validation',
    );
    expect(runButton).toBeDefined();
  });

  it('shows an error state with retry when the report cannot be loaded', async () => {
    getLatestReport = vi
      .fn()
      .mockReturnValueOnce(serverError())
      .mockReturnValueOnce(of(makeValidationReport({ caseId })));
    const fixture = await render();
    const el = element(fixture);

    expect(el.textContent).toContain('The validation report could not be loaded.');

    const retry = [...el.querySelectorAll('button')].find(
      (button) => button.textContent?.trim() === 'Retry',
    );
    retry!.click();
    await fixture.whenStable();

    expect(getLatestReport).toHaveBeenCalledTimes(2);
    expect(el.querySelector('.report-summary')).not.toBeNull();
  });

  it('runs validation and renders the new report', async () => {
    const report = makeValidationReport({
      caseId,
      items: [makeValidationItem({ requirement: 'Site plan', status: 'Missing' })],
    });
    getLatestReport = vi.fn(() => notFound());
    runValidation = vi.fn(() => of(report));
    const fixture = await render();
    const el = element(fixture);

    const runButton = [...el.querySelectorAll('button')].find(
      (button) => button.textContent?.trim() === 'Run validation',
    );
    runButton!.click();
    await fixture.whenStable();

    expect(runValidation).toHaveBeenCalledWith(caseId);
    expect(el.textContent).toContain('Site plan');
    expect(el.textContent).toContain('Missing');

    const rerunButton = [...el.querySelectorAll('button')].find(
      (button) => button.textContent?.trim() === 'Re-run validation',
    );
    expect(rerunButton).toBeDefined();
  });

  it('offers a re-run action that replaces the current report', async () => {
    const first = makeValidationReport({
      caseId,
      items: [makeValidationItem({ requirement: 'Site plan', status: 'Missing' })],
    });
    const second = makeValidationReport({
      caseId,
      items: [makeValidationItem({ requirement: 'Site plan', status: 'Complete' })],
    });
    getLatestReport = vi.fn(() => of(first));
    runValidation = vi.fn(() => of(second));
    const fixture = await render();
    const el = element(fixture);

    expect(el.textContent).toContain('Missing');

    const rerunButton = [...el.querySelectorAll('button')].find(
      (button) => button.textContent?.trim() === 'Re-run validation',
    );
    rerunButton!.click();
    await fixture.whenStable();

    expect(el.textContent).toContain('Complete');
    expect(el.textContent).not.toContain('Missing');
  });

  it('shows an error message when running validation fails', async () => {
    getLatestReport = vi.fn(() => notFound());
    runValidation = vi.fn(() => serverError());
    const fixture = await render();
    const el = element(fixture);

    const runButton = [...el.querySelectorAll('button')].find(
      (button) => button.textContent?.trim() === 'Run validation',
    );
    runButton!.click();
    await fixture.whenStable();

    expect(el.textContent).toContain('Validation could not be run.');
    expect(el.textContent).toContain('No validation report yet.');
  });

  describe('evidence viewer', () => {
    function viewButton(fixture: Awaited<ReturnType<typeof render>>): HTMLButtonElement | undefined {
      return [...element(fixture).querySelectorAll('button')].find((button) =>
        button.textContent?.trim().startsWith('View'),
      );
    }

    it('offers to open the source document for a finding that names one', async () => {
      getLatestReport = vi.fn(() =>
        of(
          makeValidationReport({
            items: [
              makeValidationItem({
                requirement: 'Applicant signature',
                documentId: 'doc-7',
                documentType: 'PermitApplication',
                pageNumber: 3,
              }),
            ],
          }),
        ),
      );
      const fixture = await render();

      expect(viewButton(fixture)?.textContent).toContain('View page 3');
    });

    it('offers no viewer for a finding with no source document', async () => {
      getLatestReport = vi.fn(() =>
        of(makeValidationReport({ items: [makeValidationItem({ documentId: null })] })),
      );
      const fixture = await render();

      expect(viewButton(fixture)).toBeUndefined();
    });

    it('opens the evidence as a case document at the cited page', async () => {
      getLatestReport = vi.fn(() =>
        of(
          makeValidationReport({
            items: [
              makeValidationItem({
                requirement: 'Applicant signature',
                documentId: 'doc-7',
                documentType: 'PermitApplication',
                pageNumber: 3,
              }),
            ],
          }),
        ),
      );
      const fixture = await render();

      viewButton(fixture)!.click();
      await fixture.whenStable();

      const viewer = element(fixture).querySelector('.document-viewer');
      expect(viewer).not.toBeNull();
      // Validation findings are always about what the applicant submitted.
      expect(viewer?.querySelector('.document-viewer__source')?.textContent).toContain(
        'Applicant submission',
      );
      expect(viewer?.textContent).toContain('Permit Application');
      expect(viewer?.textContent).toContain('Page 3');
    });
  });
});
