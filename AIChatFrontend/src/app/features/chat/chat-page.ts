import { Component, inject } from '@angular/core';

import { AuthService } from '../../core/auth/auth.service';

/**
 * Phase 5 scaffold: proves the route is protected and the session works.
 * Replaced by the real chat dashboard in Phase 7.
 */
@Component({
  selector: 'app-chat-page',
  template: `
    <main class="page">
      <section class="card">
        <h1>Signed in</h1>
        @if (auth.user(); as user) {
          <p>{{ user.fullName }}</p>
          <p class="muted">{{ user.email }}</p>
        }
        <p class="muted">This protected page is replaced by the chat dashboard in Phase 7.</p>
        <button type="button" class="btn" (click)="auth.logout()">Log out</button>
      </section>
    </main>
  `,
  styles: `
    .page { min-height: 100dvh; display: grid; place-items: center; padding: 16px; }
    .card { width: 100%; max-width: 400px; padding: 32px; border: 1px solid var(--border);
            border-radius: var(--radius-lg); background: var(--surface); text-align: center; }
    h1 { margin: 0 0 12px; font-size: 1.5rem; }
    p { margin: 0 0 8px; }
    .btn { margin-top: 16px; width: 100%; }
  `,
})
export class ChatPage {
  protected readonly auth = inject(AuthService);
}
