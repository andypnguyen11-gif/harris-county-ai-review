import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { CaseService } from '../../core/services/case.service';
import { QuestionAnsweringService } from '../../core/services/question-answering.service';
import { makeCase } from '../../testing/case-fixtures';
import { makeQuestionResponse } from '../../testing/question-answer-fixtures';
import { QuestionAnswering } from './question-answering';

describe('QuestionAnswering scope selection', () => {
  let ask: ReturnType<typeof vi.fn>;
  let getCases: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<QuestionAnswering>;

  beforeEach(() => {
    ask = vi.fn(() => of(makeQuestionResponse()));
    getCases = vi.fn(() => of([makeCase({ id: 'case-1' }), makeCase({ id: 'case-2' })]));
  });

  async function setup(): Promise<void> {
    TestBed.configureTestingModule({
      imports: [QuestionAnswering],
      providers: [
        { provide: QuestionAnsweringService, useValue: { ask } },
        { provide: CaseService, useValue: { getCases } },
      ],
    });
    fixture = TestBed.createComponent(QuestionAnswering);
    await fixture.whenStable();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  async function selectCaseScope(): Promise<void> {
    const radio = el().querySelector<HTMLInputElement>('#qa-scope-case');
    expect(radio).not.toBeNull();
    radio!.click();
    radio!.dispatchEvent(new Event('change'));
    await fixture.whenStable();
  }

  function setQuestion(value: string): void {
    const textarea = el().querySelector<HTMLTextAreaElement>('#qa-question');
    textarea!.value = value;
    textarea!.dispatchEvent(new Event('input'));
  }

  function selectCase(id: string): void {
    const select = el().querySelector<HTMLSelectElement>('#qa-case');
    expect(select).not.toBeNull();
    select!.value = id;
    select!.dispatchEvent(new Event('change'));
  }

  async function submit(): Promise<void> {
    el().querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  it('defaults to the county scope with no case picker shown', async () => {
    await setup();

    expect(el().querySelector<HTMLInputElement>('#qa-scope-county')?.checked).toBe(true);
    expect(el().querySelector('#qa-case')).toBeNull();
    expect(getCases).not.toHaveBeenCalled();
  });

  it('loads and shows the case list when the case scope is selected', async () => {
    await setup();

    await selectCaseScope();

    expect(getCases).toHaveBeenCalledTimes(1);
    const options = Array.from(el().querySelectorAll<HTMLOptionElement>('#qa-case option'));
    // Placeholder plus the two cases.
    expect(options).toHaveLength(3);
    expect(options[1].value).toBe('case-1');
  });

  it('does not reload the case list when switching scopes back and forth', async () => {
    await setup();

    await selectCaseScope();
    el().querySelector<HTMLInputElement>('#qa-scope-county')!.click();
    el().querySelector<HTMLInputElement>('#qa-scope-county')!.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    await selectCaseScope();

    expect(getCases).toHaveBeenCalledTimes(1);
  });

  it('requires a case before asking a case-scoped question', async () => {
    await setup();
    await selectCaseScope();
    setQuestion('Who signed this application?');

    await submit();

    expect(ask).not.toHaveBeenCalled();
    expect(el().querySelector('#qa-case-error')?.textContent).toContain(
      'Select the case the question is about.',
    );
  });

  it('sends the scope and the selected case with the question', async () => {
    await setup();
    await selectCaseScope();
    setQuestion('Who signed this application?');
    selectCase('case-2');

    await submit();

    expect(ask).toHaveBeenCalledWith('Who signed this application?', {
      scope: 'Case',
      caseId: 'case-2',
    });
  });

  it('asks county questions without scope options', async () => {
    await setup();
    setQuestion('What does the county require?');

    await submit();

    expect(ask).toHaveBeenCalledWith('What does the county require?');
  });

  it('shows an error with retry when the case list fails to load', async () => {
    getCases = vi.fn(() => throwError(() => new Error('boom')));
    await setup();

    await selectCaseScope();

    expect(el().querySelector('#qa-case')).toBeNull();
    const error = el().querySelector('.form-field__error');
    expect(error?.textContent).toContain('The case list could not be loaded.');

    getCases.mockReturnValue(of([makeCase({ id: 'case-9' })]));
    error!.querySelector<HTMLButtonElement>('button')!.click();
    await fixture.whenStable();

    expect(el().querySelector('#qa-case')).not.toBeNull();
  });

  it('shows the case-scoped insufficient-evidence hint', async () => {
    ask = vi.fn(() =>
      of(
        makeQuestionResponse({
          outcome: 'InsufficientEvidence',
          answer: 'The case documents do not mention a drainage plan.',
          citations: [],
        }),
      ),
    );
    await setup();
    await selectCaseScope();
    setQuestion('Did the applicant submit a drainage plan?');
    selectCase('case-1');

    await submit();

    const panel = el().querySelector('.insufficient-evidence');
    expect(panel?.textContent).toContain("case's uploaded documents");
  });
});
