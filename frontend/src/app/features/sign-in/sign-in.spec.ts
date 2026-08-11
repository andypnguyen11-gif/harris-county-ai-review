import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { AuthService } from '../../core/auth/auth.service';
import { makeSession } from '../../testing/auth-fixtures';
import { SignIn } from './sign-in';

describe('SignIn', () => {
  let signIn: ReturnType<typeof vi.fn>;

  function setup(returnUrl: string | null = null): void {
    signIn ??= vi.fn(() => of(makeSession()));
    TestBed.configureTestingModule({
      imports: [SignIn],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { signIn } },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { queryParamMap: new Map(returnUrl === null ? [] : [['returnUrl', returnUrl]]) },
          },
        },
      ],
    });
  }

  afterEach(() => {
    signIn = undefined as unknown as ReturnType<typeof vi.fn>;
  });

  async function submit() {
    const fixture = TestBed.createComponent(SignIn);
    await fixture.whenStable();
    const form = (fixture.nativeElement as HTMLElement).querySelector('form')!;
    form.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    return fixture;
  }

  it('signs in with the entered username and lands on the dashboard', async () => {
    setup();
    const router = TestBed.inject(Router);
    const navigateByUrl = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    await submit();

    expect(signIn).toHaveBeenCalledWith('dev.reviewer');
    expect(navigateByUrl).toHaveBeenCalledWith('/');
  });

  it('returns the user to the page they were trying to reach', async () => {
    setup('/cases/abc');
    const router = TestBed.inject(Router);
    const navigateByUrl = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    await submit();

    expect(navigateByUrl).toHaveBeenCalledWith('/cases/abc');
  });

  it('explains a username that is not on the allow list', async () => {
    signIn = vi.fn(() =>
      throwError(() => new HttpErrorResponse({ status: 400, statusText: 'Bad Request' })),
    );
    setup();

    const fixture = await submit();
    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('.form-error-banner')?.textContent).toContain(
      'not in the development user allow list',
    );
  });

  it('explains that development sign-in is disabled when the endpoint is absent', async () => {
    signIn = vi.fn(() =>
      throwError(() => new HttpErrorResponse({ status: 404, statusText: 'Not Found' })),
    );
    setup();

    const fixture = await submit();
    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('.form-error-banner')?.textContent).toContain(
      'Microsoft Entra ID',
    );
  });

  it('reports an unexpected failure without navigating', async () => {
    signIn = vi.fn(() =>
      throwError(() => new HttpErrorResponse({ status: 500, statusText: 'Server Error' })),
    );
    setup();
    const router = TestBed.inject(Router);
    const navigateByUrl = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    const fixture = await submit();
    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('.form-error-banner')?.textContent).toContain('Sign-in failed');
    expect(navigateByUrl).not.toHaveBeenCalled();
  });

  it('does not call the API when the username is blank', async () => {
    setup();
    const fixture = TestBed.createComponent(SignIn);
    await fixture.whenStable();
    const input = (fixture.nativeElement as HTMLElement).querySelector('input')!;
    input.value = '';
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    const form = (fixture.nativeElement as HTMLElement).querySelector('form')!;
    form.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(signIn).not.toHaveBeenCalled();
  });
});
