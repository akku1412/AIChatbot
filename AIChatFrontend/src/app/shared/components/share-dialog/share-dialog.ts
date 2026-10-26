import { DatePipe } from '@angular/common';
import { Component, ElementRef, afterNextRender, inject, input, output, signal, viewChild } from '@angular/core';

import { errorMessage } from '../../../core/http/api-error';
import { ShareStatus } from '../../../core/models/chat.models';
import { ChatApiService } from '../../../core/services/chat-api.service';
import { LoadingSpinner } from '../loading-spinner/loading-spinner';
import { NotificationService } from '../toast/notification.service';

/**
 * "Share chat": creates a read-only link to a snapshot of the chat, shows it once (only its hash is kept on the
 * server), and lets the owner replace it or stop sharing.
 */
@Component({
  selector: 'app-share-dialog',
  imports: [DatePipe, LoadingSpinner],
  host: { '(document:keydown.escape)': 'closed.emit()' },
  template: `
    <div class="backdrop" (click)="closed.emit()" aria-hidden="true"></div>
    <div class="dialog" role="dialog" aria-modal="true" aria-labelledby="share-title">
      <header>
        <h2 id="share-title">Share chat</h2>
        <button #closeButton type="button" class="close" aria-label="Close" (click)="closed.emit()">
          <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"/></svg>
        </button>
      </header>

      @if (link(); as url) {
        <p>Anyone with this link can view a read-only copy of the chat as it is now.</p>
        <div class="link-row">
          <input type="text" readonly [value]="url" aria-label="Share link" (focus)="$any($event.target).select()" />
          <button type="button" class="btn primary" (click)="copy(url)">{{ copied() ? 'Copied' : 'Copy link' }}</button>
        </div>
        <p class="note">Copy it now: for your security, this link can't be shown again. You can always create a new one.</p>
      } @else if (status(); as s) {
        @if (s.isShared) {
          <p>
            Shared on {{ s.sharedAt | date: 'medium' }} ({{ s.messageCount }} messages). The link can't be shown again;
            a new link replaces it, and the old one stops working.
          </p>
        } @else {
          <p>Anyone with the link will be able to view a read-only copy of this chat as it is now.</p>
          <ul class="facts">
            <li>Messages you send after sharing stay private.</li>
            <li>Attached files are listed by name only; they can't be downloaded.</li>
            <li>You can stop sharing at any time.</li>
          </ul>
        }
      } @else if (loadError(); as error) {
        <p class="error" role="alert">{{ error }}</p>
      } @else {
        <p class="muted"><app-loading-spinner [size]="14" label="Loading" /> Checking…</p>
      }

      <div class="actions">
        @if (status()?.isShared || link()) {
          <button type="button" class="btn danger-outline" [disabled]="busy()" (click)="stop()">Stop sharing</button>
        }
        @if (status()) {
          <button type="button" class="btn primary" [disabled]="busy()" (click)="create()">
            @if (busy()) {
              <app-loading-spinner [size]="14" label="Working" />
            }
            {{ status()!.isShared || link() ? 'Create new link' : 'Create link' }}
          </button>
        }
      </div>
    </div>
  `,
  styles: `
    .backdrop { position: fixed; inset: 0; z-index: 1000; background: rgb(0 0 0 / 0.45); }
    .dialog {
      position: fixed; z-index: 1001; top: 50%; left: 50%; transform: translate(-50%, -50%);
      width: min(480px, calc(100vw - 32px)); max-height: calc(100vh - 32px); overflow: auto; box-sizing: border-box;
      padding: 20px 22px; border: 1px solid var(--border); border-radius: 16px; background: var(--surface-raised);
      box-shadow: 0 16px 48px rgb(0 0 0 / 0.25); color: var(--text);
    }
    header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 8px; }
    h2 { margin: 0; font-size: 1.125rem; }
    p { margin: 0 0 12px; line-height: 1.5; }
    .facts { margin: 0 0 12px; padding-left: 20px; color: var(--text-muted); font-size: 0.875rem; line-height: 1.6; }
    .note { font-size: 0.8125rem; color: var(--text-muted); }
    .muted { color: var(--text-muted); display: flex; align-items: center; gap: 8px; }
    .error { color: var(--danger); }
    .close {
      width: 32px; height: 32px; display: grid; place-items: center; border: 0; border-radius: 8px;
      background: transparent; color: var(--text); cursor: pointer;
    }
    .close:hover { background: var(--surface-hover); }
    .link-row { display: flex; gap: 8px; margin-bottom: 8px; }
    .link-row input {
      flex: 1; min-width: 0; padding: 8px 10px; border: 1px solid var(--border); border-radius: 10px;
      background: var(--surface); color: var(--text); font: inherit; font-size: 0.8125rem;
    }
    .actions { display: flex; justify-content: flex-end; gap: 8px; margin-top: 8px; flex-wrap: wrap; }
    .btn {
      display: inline-flex; align-items: center; gap: 8px; padding: 8px 14px; border: 1px solid transparent; border-radius: 10px;
      font: inherit; font-weight: 600; font-size: 0.875rem; cursor: pointer; white-space: nowrap;
    }
    .btn:disabled { opacity: 0.55; cursor: not-allowed; }
    .primary { background: var(--brand-gradient); color: #fff; }
    .danger-outline { background: transparent; border-color: color-mix(in srgb, var(--danger) 45%, transparent); color: var(--danger); }
    .danger-outline:hover:not(:disabled) { background: color-mix(in srgb, var(--danger) 10%, transparent); }
  `,
})
export class ShareDialog {
  private readonly api = inject(ChatApiService);
  private readonly notifications = inject(NotificationService);

  readonly conversationId = input.required<number>();
  readonly closed = output<void>();

  protected readonly status = signal<ShareStatus | null>(null);
  protected readonly loadError = signal<string | null>(null);
  /** The full link, only right after creating it. */
  protected readonly link = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly copied = signal(false);
  private readonly closeButton = viewChild<ElementRef<HTMLButtonElement>>('closeButton');

  constructor() {
    afterNextRender(() => {
      this.closeButton()?.nativeElement.focus();
      this.api.getShareStatus(this.conversationId()).subscribe({
        next: (status) => this.status.set(status),
        error: (err: unknown) => this.loadError.set(`Couldn't check sharing. ${errorMessage(err)}`),
      });
    });
  }

  protected create(): void {
    this.busy.set(true);
    this.api.shareConversation(this.conversationId()).subscribe({
      next: (created) => {
        this.busy.set(false);
        this.copied.set(false);
        this.link.set(`${window.location.origin}/share/${created.token}`);
        this.status.set({ isShared: true, sharedAt: created.sharedAt, messageCount: created.messageCount });
      },
      error: (err: unknown) => {
        this.busy.set(false);
        this.notifications.error(`Couldn't create the link. ${errorMessage(err)}`);
      },
    });
  }

  protected stop(): void {
    this.busy.set(true);
    this.api.revokeShare(this.conversationId()).subscribe({
      next: () => {
        this.busy.set(false);
        this.link.set(null);
        this.status.set({ isShared: false, sharedAt: null, messageCount: null });
        this.notifications.success('Sharing stopped. The link no longer works.');
      },
      error: (err: unknown) => {
        this.busy.set(false);
        this.notifications.error(`Couldn't stop sharing. ${errorMessage(err)}`);
      },
    });
  }

  protected async copy(url: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(url);
      this.copied.set(true);
    } catch {
      this.notifications.error("Couldn't copy automatically. Select the link and copy it.");
    }
  }
}
