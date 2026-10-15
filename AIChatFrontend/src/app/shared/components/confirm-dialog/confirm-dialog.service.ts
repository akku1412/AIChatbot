import { Injectable, signal } from '@angular/core';

export interface ConfirmOptions {
  title: string;
  message: string;
  confirmText?: string;
  cancelText?: string;
  /** Styles the confirm button as destructive (e.g. delete). */
  danger?: boolean;
}

interface PendingConfirm extends ConfirmOptions {
  resolve: (confirmed: boolean) => void;
}

/** `if (await confirmDialog.confirm({...}))` — rendered by <app-confirm-dialog> in the root component. */
@Injectable({ providedIn: 'root' })
export class ConfirmDialogService {
  private readonly pending = signal<PendingConfirm | null>(null);
  readonly current = this.pending.asReadonly();

  confirm(options: ConfirmOptions): Promise<boolean> {
    // A new request replaces (and cancels) any open one.
    this.pending()?.resolve(false);
    return new Promise<boolean>((resolve) => this.pending.set({ ...options, resolve }));
  }

  close(confirmed: boolean): void {
    const request = this.pending();
    this.pending.set(null);
    request?.resolve(confirmed);
  }
}
