import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';

import { QuestionResponse } from '../../core/models/question-answer.model';
import { QuestionAnsweringService } from '../../core/services/question-answering.service';
import { makeCitation, makeQuestionResponse } from '../../testing/question-answer-fixtures';
import { QuestionAnswering } from './question-answering';

describe('QuestionAnswering', () => {
  let ask: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<QuestionAnswering>;

  async function setup(): Promise<void> {
    TestBed.configureTestingModule({
      imports: [QuestionAnswering],
      providers: [{ provide: QuestionAnsweringService, useValue: { ask } }],
    });
    fixture = TestBed.createComponent(QuestionAnswering);
    await fixture.whenStable();
  }

  beforeEach(() => {
    ask = vi.fn(() => of(makeQuestionResponse()));
  });

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function setQuestion(value: string): void {
    const textarea = el().querySelector<HTMLTextAreaElement>('#qa-question');
    expect(textarea).not.toBeNull();
    textarea!.value = value;
    textarea!.dispatchEvent(new Event('input'));
  }

  async function submit(): Promise<void> {
    const form = el().querySelector<HTMLFormElement>('form');
    form!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  it('renders the question form with an empty result area', async () => {
    await setup();

    expect(el().querySelector('#qa-question')).not.toBeNull();
    expect(el().querySelector('.answer-card')).toBeNull();
    expect(el().querySelector('.insufficient-evidence')).toBeNull();
  });

  it('does not call the service when the question is empty', async () => {
    await setup();

    await submit();

    expect(ask).not.toHaveBeenCalled();
    expect(el().querySelector('.form-field__error')?.textContent).toContain(
      'A question is required.',
    );
  });

  it('does not call the service when the question is only whitespace', async () => {
    await setup();
    setQuestion('   ');

    await submit();

    expect(ask).not.toHaveBeenCalled();
  });

  it('shows a loading state while the question is in flight and disables the button', async () => {
    const pending = new Subject<QuestionResponse>();
    ask = vi.fn(() => pending.asObservable());
    await setup();
    setQuestion('What is required?');

    await submit();

    expect(el().querySelector('.state-panel[role="status"]')?.textContent).toContain(
      'Searching the Harris County reference corpus',
    );
    expect(el().querySelector<HTMLButtonElement>('button[type="submit"]')?.disabled).toBe(true);

    pending.next(makeQuestionResponse());
    pending.complete();
    await fixture.whenStable();

    expect(el().querySelector<HTMLButtonElement>('button[type="submit"]')?.disabled).toBe(false);
  });

  it('sends the trimmed question to the service', async () => {
    await setup();
    setQuestion('  What is required?  ');

    await submit();

    expect(ask).toHaveBeenCalledWith('What is required?');
  });

  it('renders the answer with its citations', async () => {
    ask = vi.fn(() =>
      of(
        makeQuestionResponse({
          answer: 'A completed application form and two site plan sets are required.',
          citations: [
            makeCitation({
              number: 1,
              title: 'Floodplain Regulations',
              section: 'Section 4.2',
              page: 17,
              sourceUrl: 'https://www.hcfcd.org/regulations',
            }),
            makeCitation({ number: 2, title: 'Submittal Checklist', section: null, page: null, sourceUrl: null }),
          ],
        }),
      ),
    );
    await setup();
    setQuestion('What must an application include?');

    await submit();

    expect(el().querySelector('.answer-text')?.textContent).toContain(
      'A completed application form and two site plan sets are required.',
    );
    const citations = Array.from(el().querySelectorAll('.citation'));
    expect(citations).toHaveLength(2);
    expect(citations[0].textContent).toContain('[1]');
    expect(citations[0].textContent).toContain('Floodplain Regulations');
    expect(citations[0].textContent).toContain('Section 4.2');
    expect(citations[0].textContent).toContain('Page 17');
    const link = citations[0].querySelector<HTMLAnchorElement>('a.citation__title');
    expect(link?.getAttribute('href')).toBe('https://www.hcfcd.org/regulations');
    expect(citations[1].textContent).toContain('Submittal Checklist');
    expect(citations[1].querySelector('a')).toBeNull();
    expect(citations[1].textContent).not.toContain('Page');
  });

  it('shows the asked question alongside the answer', async () => {
    await setup();
    setQuestion('What is required?');

    await submit();

    expect(el().querySelector('.asked-question')?.textContent).toContain('What is required?');
  });

  it('renders the insufficient-evidence state instead of an answer', async () => {
    ask = vi.fn(() =>
      of(
        makeQuestionResponse({
          outcome: 'InsufficientEvidence',
          answer: 'The corpus does not cover permit fees.',
          citations: [],
        }),
      ),
    );
    await setup();
    setQuestion('What are the permit fees?');

    await submit();

    const panel = el().querySelector('.insufficient-evidence');
    expect(panel).not.toBeNull();
    expect(panel?.textContent).toContain('Not enough evidence to answer');
    expect(panel?.textContent).toContain('The corpus does not cover permit fees.');
    expect(el().querySelector('.answer-card')).toBeNull();
  });

  it('shows an error state when the API call fails', async () => {
    ask = vi.fn(() => throwError(() => new Error('boom')));
    await setup();
    setQuestion('What is required?');

    await submit();

    expect(el().querySelector('.state-panel--error')?.textContent).toContain(
      'The question could not be answered',
    );
    expect(el().querySelector('.answer-card')).toBeNull();
  });

  it('clears a previous error when a new question succeeds', async () => {
    ask = vi.fn(() => throwError(() => new Error('boom')));
    await setup();
    setQuestion('What is required?');
    await submit();
    expect(el().querySelector('.state-panel--error')).not.toBeNull();

    ask.mockReturnValue(of(makeQuestionResponse()));
    await submit();

    expect(el().querySelector('.state-panel--error')).toBeNull();
    expect(el().querySelector('.answer-card')).not.toBeNull();
  });
});
