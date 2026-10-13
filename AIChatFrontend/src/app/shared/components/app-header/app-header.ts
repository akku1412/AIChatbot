import { Component, input, output } from '@angular/core';

/** Top bar of the chat area. The menu and new-chat buttons only show on small screens. */
@Component({
  selector: 'app-header',
  template: `
    <header class="header">
      <button type="button" class="icon-button mobile" aria-label="Open sidebar" (click)="menu.emit()">
        <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true"><path d="M4 7h16M4 12h16M4 17h16" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>
      </button>
      <h1 class="title" [title]="title()">{{ title() }}</h1>
      <button type="button" class="icon-button mobile" aria-label="New chat" (click)="newChat.emit()">
        <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true"><path d="M12 5v14M5 12h14" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>
      </button>
    </header>
  `,
  styles: `
    .header {
      height: 56px; display: flex; align-items: center; gap: 8px; padding: 0 12px;
      border-bottom: 1px solid color-mix(in srgb, var(--border) 60%, transparent); background: color-mix(in srgb, var(--bg) 85%, transparent); backdrop-filter: blur(8px);
    }
    .title {
      flex: 1; margin: 0; font-size: 1rem; font-weight: 600; text-align: center;
      white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
    }
    .icon-button {
      width: 36px; height: 36px; display: grid; place-items: center; border: 0; border-radius: 8px;
      background: transparent; color: var(--text); cursor: pointer;
    }
    .icon-button:hover { background: var(--surface-hover); }
    .mobile { visibility: hidden; }
    @media (max-width: 768px) { .mobile { visibility: visible; } }
  `,
})
export class AppHeader {
  readonly title = input('AIChat');
  readonly menu = output<void>();
  readonly newChat = output<void>();
}
