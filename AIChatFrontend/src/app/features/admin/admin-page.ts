import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { ChatStore } from '../../core/services/chat.store';

/** /admin — tabs for users, usage and feedback (admins only: adminGuard here, the role check on the API). */
@Component({
  selector: 'app-admin-page',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  template: `
    <div class="page">
      <div class="column">
        <header class="page-head">
          <h1>Admin</h1>
          <p class="muted">Accounts, usage and feedback across all users. Days are UTC.</p>
        </header>
        <nav class="tabs" aria-label="Admin sections">
          <a routerLink="users" routerLinkActive="active" ariaCurrentWhenActive="page">Users</a>
          <a routerLink="usage" routerLinkActive="active" ariaCurrentWhenActive="page">Usage</a>
          <a routerLink="feedback" routerLinkActive="active" ariaCurrentWhenActive="page">Feedback</a>
        </nav>
        <router-outlet />
      </div>
    </div>
  `,
  styles: `
    :host { flex: 1; min-height: 0; overflow-y: auto; }
    .page { padding: 32px 16px 48px; }
    .column { max-width: 1040px; margin: 0 auto; display: flex; flex-direction: column; gap: 16px; }
    .page-head h1 { margin: 0 0 4px; font-size: 1.625rem; font-weight: 700; }
    .page-head p { margin: 0; }
    .muted { color: var(--text-muted); }
    .tabs { display: flex; gap: 4px; border-bottom: 1px solid var(--border); }
    .tabs a {
      padding: 8px 14px; margin-bottom: -1px; border-bottom: 2px solid transparent;
      color: var(--text-muted); font-weight: 600; font-size: 0.9rem; text-decoration: none;
    }
    .tabs a:hover { color: var(--text); }
    .tabs a.active { color: var(--text); border-bottom-color: var(--accent); }
  `,
})
export class AdminPage {
  constructor() {
    // Nothing is "open" in the sidebar while on the admin pages.
    inject(ChatStore).startNewChat();
  }
}
