import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('renders the brand and primary navigation', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('.app-header__brand-title')?.textContent).toContain(
      'Harris County AI Document Review',
    );

    const navLinks = Array.from(compiled.querySelectorAll('nav.app-nav a')).map((a) =>
      a.textContent?.trim(),
    );
    expect(navLinks).toEqual(['Dashboard', 'Cases', 'Knowledge Base']);
  });

  it('renders a router outlet and footer', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('router-outlet')).not.toBeNull();
    expect(compiled.querySelector('footer.app-footer')).not.toBeNull();
  });
});
