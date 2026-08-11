import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { CaseService } from '../../../core/services/case.service';
import { makeCase } from '../../../testing/case-fixtures';
import { CaseList } from './case-list';

describe('CaseList', () => {
  let getCases: ReturnType<typeof vi.fn>;

  function setup(): void {
    TestBed.configureTestingModule({
      imports: [CaseList],
      providers: [provideRouter([]), { provide: CaseService, useValue: { getCases } }],
    });
  }

  async function render() {
    const fixture = TestBed.createComponent(CaseList);
    await fixture.whenStable();
    return fixture;
  }

  it('renders a table row per case with case number, name, and status', async () => {
    const cases = [
      makeCase({ caseNumber: 'HC-2026-0001', name: 'Cypress Creek Culvert', status: 'New' }),
      makeCase({ caseNumber: 'HC-2026-0002', name: 'Addicks Reservoir Pad', status: 'InReview' }),
    ];
    getCases = vi.fn(() => of(cases));
    setup();
    const fixture = await render();
    const el = fixture.nativeElement as HTMLElement;

    const rows = el.querySelectorAll('tbody tr');
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('HC-2026-0001');
    expect(rows[0].textContent).toContain('Cypress Creek Culvert');
    expect(rows[0].textContent).toContain('New');
    expect(rows[1].textContent).toContain('HC-2026-0002');
    expect(rows[1].textContent).toContain('In Review');
  });

  it('shows the empty state with a create call-to-action when there are no cases', async () => {
    getCases = vi.fn(() => of([]));
    setup();
    const fixture = await render();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('table')).toBeNull();
    expect(el.textContent).toContain('No cases yet');
    const cta = el.querySelector('.state-panel a.btn');
    expect(cta?.getAttribute('href')).toBe('/cases/new');
  });

  it('shows an error state and retries loading on demand', async () => {
    getCases = vi
      .fn()
      .mockReturnValueOnce(throwError(() => new Error('network')))
      .mockReturnValue(of([makeCase({ caseNumber: 'HC-2026-0009' })]));
    setup();
    const fixture = await render();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('Unable to load cases');

    const retry = el.querySelector<HTMLButtonElement>('.state-panel--error button');
    expect(retry).not.toBeNull();
    retry?.click();
    await fixture.whenStable();

    expect(getCases).toHaveBeenCalledTimes(2);
    expect(el.querySelectorAll('tbody tr')).toHaveLength(1);
    expect(el.textContent).toContain('HC-2026-0009');
  });
});
