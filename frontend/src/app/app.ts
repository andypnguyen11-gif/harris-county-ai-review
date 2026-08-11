import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { AuthService } from './core/auth/auth.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly currentYear = new Date().getFullYear();
  protected readonly user = this.auth.user;
  protected readonly isAuthenticated = this.auth.isAuthenticated;

  protected signOut(): void {
    this.auth.signOut();
    void this.router.navigate(['/sign-in']);
  }
}
