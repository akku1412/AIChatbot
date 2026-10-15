import { Component } from '@angular/core';

import { AppLogo } from '../app-logo/app-logo';

/**
 * Split layout for the sign-in pages: a brand panel (hidden on small screens) and the form area,
 * which receives the page content through <ng-content>.
 */
@Component({
  selector: 'app-auth-shell',
  imports: [AppLogo],
  template: `
    <div class="shell">
      <aside class="brand" aria-hidden="true">
        <div class="brand-top">
          <app-logo [size]="36" />
          <span class="brand-name">AIChat</span>
        </div>

        <div class="pitch">
          <h2>Your AI assistant,<br />private by design.</h2>
          <ul class="features">
            <li>
              <span class="icon">
                <svg viewBox="0 0 24 24" width="18" height="18"><path d="M12 3l7 3v5c0 4.5-3 8.5-7 10-4-1.5-7-5.5-7-10V6z" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"/><path d="M9 12l2 2 4-4" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>
              </span>
              <div><strong>Passwordless sign-in</strong><span>Continue with Google, or with a one-time code sent to your email.</span></div>
            </li>
            <li>
              <span class="icon">
                <svg viewBox="0 0 24 24" width="18" height="18"><rect x="4" y="10" width="16" height="10" rx="2" fill="none" stroke="currentColor" stroke-width="1.8"/><path d="M8 10V7a4 4 0 0 1 8 0v3" fill="none" stroke="currentColor" stroke-width="1.8"/></svg>
              </span>
              <div><strong>Your conversations stay yours</strong><span>Every chat is private to your account.</span></div>
            </li>
            <li>
              <span class="icon">
                <svg viewBox="0 0 24 24" width="18" height="18"><path d="M8 8l-4 4 4 4M16 8l4 4-4 4M13.5 5l-3 14" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>
              </span>
              <div><strong>Built for real work</strong><span>Formatted answers, tables and copyable code.</span></div>
            </li>
          </ul>
        </div>

        <p class="brand-foot">© {{ year }} AIChat</p>
      </aside>

      <main class="content">
        <div class="content-inner">
          <ng-content />
        </div>
      </main>
    </div>
  `,
  styleUrl: './auth-shell.css',
})
export class AuthShell {
  protected readonly year = new Date().getFullYear();
}
