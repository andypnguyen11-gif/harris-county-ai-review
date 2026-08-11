import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { CaseService } from '../../../core/services/case.service';
import { makeCase } from '../../../testing/case-fixtures';
import { CaseDetail } from './case-detail';

describe('CaseDetail', () => {
  let getCase: ReturnType<typeof vi.fn>;

  function setup(id: string): void {
    TestBed.configureTestingModule({
      imports: [CaseDetail],
      providers: [
        provideRouter([]),
        { provide: CaseService, useValue: { getCase } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: new Map([['id', id]]) } },
        },
      ],
    });
  }

  async function render() {
    const fixture = TestBed.createComponent(CaseDetail);
    await fixture.whenStable();
    return fixture;
  }

  it('loads the case by route id and renders header, metadata, and placeholders', async () => {
    const caseItem = makeCase({
      name: 'Cypress Creek Culvert',
      caseNumber: 'HC-2026-0042',
      status: 'ReadyForReview',
    });
    getCase = vi.fn(() => of(caseItem));
    setup(caseItem.id);
    const fixture = await render();
    const el = fixture.nativeElement as HTMLElement;

    expect(getCase).toHaveBeenCalledWith(caseItem.id);
    expect(el.querySelector('h1')?.textContent).toContain('Cypress Creek Culvert');
    expect(el.textContent).toContain('HC-2026-0042');
    expect(el.textContent).toContain('Ready for Review');
    expect(el.textContent).toContain('Floodplain Development Permit');
    expect(el.textContent).toContain('Documents');
    expect(el.textContent).toContain('Validation');
  });

  it('shows an error state when the case cannot be loaded', async () => {
    getCase = vi.fn(() => throwError(() => new Error('not found')));
    setup('missing-id');
    const fixture = await render();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Unable to load case');
  });
});
