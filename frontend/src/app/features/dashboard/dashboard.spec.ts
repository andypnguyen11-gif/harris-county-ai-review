import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { CaseService } from '../../core/services/case.service';
import { makeCase } from '../../testing/case-fixtures';
import { Dashboard } from './dashboard';

describe('Dashboard', () => {
  let getCases: ReturnType<typeof vi.fn>;

  function setup(): void {
    TestBed.configureTestingModule({
      imports: [Dashboard],
      providers: [provideRouter([]), { provide: CaseService, useValue: { getCases } }],
    });
  }

  async function render() {
    const fixture = TestBed.createComponent(Dashboard);
    await fixture.whenStable();
    return fixture;
  }

  it('renders the total and per-status counts from the service', async () => {
    getCases = vi.fn(() =>
      of([
        makeCase({ status: 'New' }),
        makeCase({ status: 'New' }),
        makeCase({ status: 'Processing' }),
        makeCase({ status: 'ReadyForReview' }),
        makeCase({ status: 'Completed' }),
      ]),
    );
    setup();
    const fixture = await render();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('[data-testid="total-cases"]')?.textContent).toContain('5');
    expect(el.querySelector('[data-testid="count-New"]')?.textContent).toContain('2');
    expect(el.querySelector('[data-testid="count-Processing"]')?.textContent).toContain('1');
    expect(el.querySelector('[data-testid="count-ReadyForReview"]')?.textContent).toContain('1');
    expect(el.querySelector('[data-testid="count-InReview"]')?.textContent).toContain('0');
    expect(el.querySelector('[data-testid="count-Completed"]')?.textContent).toContain('1');
  });

  it('lists the five most recently created cases', async () => {
    getCases = vi.fn(() =>
      of([
        makeCase({ name: 'Oldest', createdAt: '2026-01-01T00:00:00Z' }),
        makeCase({ name: 'Newest', createdAt: '2026-08-01T00:00:00Z' }),
        makeCase({ name: 'Middle', createdAt: '2026-04-01T00:00:00Z' }),
      ]),
    );
    setup();
    const fixture = await render();
    const names = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('.recent__item-name'),
    ).map((a) => a.textContent?.trim());

    expect(names).toEqual(['Newest', 'Middle', 'Oldest']);
  });

  it('shows an empty state when there are no cases', async () => {
    getCases = vi.fn(() => of([]));
    setup();
    const fixture = await render();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No cases yet');
  });

  it('shows an error state when loading fails', async () => {
    getCases = vi.fn(() => throwError(() => new Error('network')));
    setup();
    const fixture = await render();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Unable to load dashboard',
    );
  });
});
