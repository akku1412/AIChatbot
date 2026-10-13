import { Component, inject } from '@angular/core';

import { NotificationService } from './notification.service';

@Component({
  selector: 'app-toast-container',
  template: `
    <div class="toasts" aria-live="polite" aria-atomic="false">
      @for (toast of notifications.notifications(); track toast.id) {
        <div class="toast" [class]="toast.kind" [attr.role]="toast.kind === 'error' ? 'alert' : 'status'">
          <span class="message">{{ toast.message }}</span>
          <button type="button" class="close" aria-label="Dismiss" (click)="notifications.dismiss(toast.id)">×</button>
        </div>
      }
    </div>
  `,
  styles: `
    .toasts {
      position: fixed; z-index: 1000; left: 50%; bottom: 24px; transform: translateX(-50%);
      display: flex; flex-direction: column; gap: 8px; width: min(420px, calc(100vw - 32px));
    }
    .toast {
      display: flex; align-items: flex-start; gap: 12px; padding: 12px 14px; border-radius: var(--radius);
      border: 1px solid var(--border); background: var(--surface); color: var(--text);
      box-shadow: 0 6px 24px rgb(0 0 0 / 0.18); animation: enter 160ms ease-out;
    }
    .toast.success { border-left: 4px solid var(--success); }
    .toast.error { border-left: 4px solid var(--danger); }
    .toast.info { border-left: 4px solid var(--accent); }
    .message { flex: 1; font-size: 0.9375rem; }
    .close { border: 0; background: none; color: var(--text-muted); font-size: 1.25rem; line-height: 1; cursor: pointer; padding: 0 2px; }
    @keyframes enter { from { opacity: 0; transform: translateY(8px); } to { opacity: 1; transform: none; } }
    @media (prefers-reduced-motion: reduce) { .toast { animation: none; } }
  `,
})
export class ToastContainer {
  protected readonly notifications = inject(NotificationService);
}
