import { Component, ElementRef, effect, inject, viewChild } from '@angular/core';

import { ConfirmDialogService } from './confirm-dialog.service';

/**
 * Native <dialog> (modal): the browser provides focus trapping, Escape to cancel and an inert background.
 * Focus starts on Cancel so a stray Enter never confirms a destructive action.
 */
@Component({
  selector: 'app-confirm-dialog',
  template: `
    <dialog #dialog class="dialog" aria-labelledby="confirm-title" aria-describedby="confirm-message"
            (cancel)="$event.preventDefault(); service.close(false)" (click)="onBackdropClick($event)">
      @if (service.current(); as request) {
        <div class="panel">
          <div class="icon" [class.danger]="request.danger" aria-hidden="true">
            @if (request.danger) {
              <svg viewBox="0 0 24 24" width="20" height="20"><path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>
            } @else {
              <svg viewBox="0 0 24 24" width="20" height="20"><circle cx="12" cy="12" r="9" fill="none" stroke="currentColor" stroke-width="1.8"/><path d="M12 8v5M12 16.5v.5" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>
            }
          </div>
          <h2 id="confirm-title">{{ request.title }}</h2>
          <p id="confirm-message">{{ request.message }}</p>
          <div class="actions">
            <button #cancelButton type="button" class="btn secondary" (click)="service.close(false)">
              {{ request.cancelText ?? 'Cancel' }}
            </button>
            <button type="button" class="btn" [class.danger]="request.danger" [class.primary]="!request.danger" (click)="service.close(true)">
              {{ request.confirmText ?? 'Confirm' }}
            </button>
          </div>
        </div>
      }
    </dialog>
  `,
  styles: `
    .dialog {
      width: min(420px, calc(100vw - 32px)); padding: 0; border: 1px solid var(--border);
      border-radius: var(--radius-lg); background: var(--surface-raised); color: var(--text); box-shadow: var(--shadow-lg);
    }
    .dialog::backdrop { background: rgb(0 0 0 / 0.5); backdrop-filter: blur(2px); }
    .dialog[open] { animation: pop 140ms ease-out; }
    .panel { padding: 24px; }
    .icon {
      width: 40px; height: 40px; display: grid; place-items: center; border-radius: 12px; margin-bottom: 14px;
      background: var(--accent-soft); color: var(--accent);
    }
    .icon.danger { background: color-mix(in srgb, var(--danger) 12%, transparent); color: var(--danger); }
    h2 { margin: 0 0 6px; font-size: 1.125rem; }
    p { margin: 0 0 22px; color: var(--text-muted); overflow-wrap: anywhere; }
    .actions { display: flex; justify-content: flex-end; gap: 10px; }
    .btn {
      padding: 9px 16px; border-radius: var(--radius); border: 1px solid transparent;
      font: inherit; font-weight: 600; cursor: pointer;
    }
    .secondary { background: transparent; border-color: var(--border); color: var(--text); }
    .secondary:hover { background: var(--surface-hover); }
    .primary { background: var(--brand-gradient); color: #fff; }
    .danger { background: var(--danger); color: #fff; }
    .danger:hover, .primary:hover { filter: brightness(1.07); }
    @keyframes pop { from { opacity: 0; transform: translateY(6px) scale(0.98); } to { opacity: 1; transform: none; } }
    @media (prefers-reduced-motion: reduce) { .dialog[open] { animation: none; } }
  `,
})
export class ConfirmDialog {
  protected readonly service = inject(ConfirmDialogService);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly cancelButton = viewChild<ElementRef<HTMLButtonElement>>('cancelButton');

  constructor() {
    effect(() => {
      const open = this.service.current() !== null;
      const dialog = this.dialog().nativeElement;
      if (open && !dialog.open) {
        dialog.showModal();
        setTimeout(() => this.cancelButton()?.nativeElement.focus());
      } else if (!open && dialog.open) {
        dialog.close();
      }
    });
  }

  /** Clicking the dimmed area outside the panel cancels. */
  protected onBackdropClick(event: MouseEvent): void {
    if (event.target === this.dialog().nativeElement) {
      this.service.close(false);
    }
  }
}
