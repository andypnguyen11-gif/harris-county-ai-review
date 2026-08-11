import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { CaseService } from '../../../core/services/case.service';
import { makeCase } from '../../../testing/case-fixtures';
import { CaseCreate } from './case-create';

describe('CaseCreate', () => {
  let createCase: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<CaseCreate>;
  let router: Router;

  async function setup(): Promise<void> {
    createCase = vi.fn();
    TestBed.configureTestingModule({
      imports: [CaseCreate],
      providers: [provideRouter([]), { provide: CaseService, useValue: { createCase } }],
    });
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(CaseCreate);
    await fixture.whenStable();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function setName(value: string): void {
    const input = el().querySelector<HTMLInputElement>('#case-name');
    expect(input).not.toBeNull();
    input!.value = value;
    input!.dispatchEvent(new Event('input'));
  }

  function submit(): void {
    el()
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  }

  it('renders the workflow type select fixed to Floodplain Development Permit', async () => {
    await setup();
    const select = el().querySelector<HTMLSelectElement>('#workflow-type');

    expect(select).not.toBeNull();
    expect(select!.value).toBe('FloodplainDevelopmentPermit');
    expect(select!.options).toHaveLength(1);
    expect(select!.options[0].textContent).toContain('Floodplain Development Permit');
  });

  it('shows a validation error and does not call the service when the name is missing', async () => {
    await setup();

    submit();
    await fixture.whenStable();

    expect(createCase).not.toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
    expect(el().textContent).toContain('Case name is required.');
  });

  it('creates the case and navigates to its detail page on success', async () => {
    await setup();
    const created = makeCase({ name: 'Smith Residence' });
    createCase.mockReturnValue(of(created));

    setName('Smith Residence');
    submit();
    await fixture.whenStable();

    expect(createCase).toHaveBeenCalledWith({
      name: 'Smith Residence',
      workflowType: 'FloodplainDevelopmentPermit',
    });
    expect(router.navigate).toHaveBeenCalledWith(['/cases', created.id]);
  });

  it('shows a submission error and stays on the form when the service fails', async () => {
    await setup();
    createCase.mockReturnValue(throwError(() => new Error('network')));

    setName('Smith Residence');
    submit();
    await fixture.whenStable();

    expect(createCase).toHaveBeenCalledTimes(1);
    expect(router.navigate).not.toHaveBeenCalled();
    expect(el().textContent).toContain('The case could not be created.');
  });
});
