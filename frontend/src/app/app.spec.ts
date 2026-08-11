import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { App } from './app';
import { AuthService } from './core/auth/auth.service';
import { clearSessionStorage, signInStorage } from './testing/auth-fixtures';

describe('App', () => {
  async function setup(): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  }

  beforeEach(() => {
    clearSessionStorage();
    TestBed.resetTestingModule();
  });

  afterEach(() => clearSessionStorage());

  it('should create the app', async () => {
    await setup();
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  async function navLinksFor(roles: string[]): Promise<(string | undefined)[]> {
    signInStorage({ roles });
    await setup();
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    return Array.from(compiled.querySelectorAll('nav.app-nav a')).map((a) => a.textContent?.trim());
  }

  it('renders the brand and the case-review navigation for a reviewer', async () => {
    signInStorage();
    await setup();
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('.app-header__brand-title')?.textContent).toContain(
      'Harris County AI Document Review',
    );

    const navLinks = Array.from(compiled.querySelectorAll('nav.app-nav a')).map((a) =>
      a.textContent?.trim(),
    );
    expect(navLinks).toEqual(['Dashboard', 'Cases', 'Ask a Question']);
  });

  it('adds the knowledge base link for an administrator', async () => {
    expect(await navLinksFor(['Administrator'])).toEqual([
      'Dashboard',
      'Cases',
      'Ask a Question',
      'Knowledge Base',
    ]);
  });

  it('hides the knowledge base link from a reviewer', async () => {
    expect(await navLinksFor(['Reviewer'])).not.toContain('Knowledge Base');
  });

  it('hides the navigation from a signed-out visitor', async () => {
    await setup();
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('nav.app-nav')).toBeNull();
    expect(compiled.querySelector('.app-user')).toBeNull();
    // The brand and shell stay, so the sign-in page still looks like the app.
    expect(compiled.querySelector('.app-header__brand-title')).not.toBeNull();
  });

  it('shows the signed-in user and signs them out', async () => {
    signInStorage({ displayName: 'Dev Reviewer' });
    await setup();
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('.app-user__name')?.textContent).toContain('Dev Reviewer');

    compiled.querySelector<HTMLButtonElement>('.app-user__sign-out')!.click();
    await fixture.whenStable();

    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/sign-in']);
    expect(compiled.querySelector('nav.app-nav')).toBeNull();
  });

  it('renders a router outlet and footer', async () => {
    await setup();
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('router-outlet')).not.toBeNull();
    expect(compiled.querySelector('footer.app-footer')).not.toBeNull();
  });
});
