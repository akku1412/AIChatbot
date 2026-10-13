import { Component, computed, inject, input, signal } from '@angular/core';

import { ChatMessage } from '../../../core/models/chat.models';
import { NotificationService } from '../toast/notification.service';

/**
 * One message. User messages are right-aligned bubbles; assistant messages are full-width text with
 * a Copy button. Content is rendered as plain text here; Phase 8 switches assistant replies to safe Markdown.
 */
@Component({
  selector: 'app-chat-message',
  template: `
    <article class="message" [class.user]="isUser()" [class.assistant]="!isUser()" [attr.aria-label]="isUser() ? 'You said' : 'AIChat replied'">
      @if (!isUser()) {
        <div class="avatar" aria-hidden="true">AI</div>
      }
      <div class="body">
        <div class="content">{{ message().content }}</div>
        @if (!isUser()) {
          <div class="actions">
            <button type="button" class="action" (click)="copy()" [attr.aria-label]="copied() ? 'Copied' : 'Copy response'">
              @if (copied()) {
                <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M5 12l5 5L20 7" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>
                <span>Copied</span>
              } @else {
                <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><rect x="9" y="9" width="11" height="11" rx="2" fill="none" stroke="currentColor" stroke-width="2"/><path d="M5 15V6a2 2 0 0 1 2-2h9" fill="none" stroke="currentColor" stroke-width="2"/></svg>
                <span>Copy</span>
              }
            </button>
          </div>
        }
      </div>
    </article>
  `,
  styles: `
    .message { display: flex; gap: 12px; padding: 12px 0; }
    .message.user { justify-content: flex-end; }
    .user .body { max-width: min(80%, 560px); }
    .user .content {
      background: var(--surface-hover); padding: 10px 16px; border-radius: 20px 20px 6px 20px;
    }
    .assistant .body { flex: 1; min-width: 0; }
    .content { white-space: pre-wrap; overflow-wrap: anywhere; line-height: 1.6; }
    .avatar {
      flex: none; width: 30px; height: 30px; border-radius: 50%; display: grid; place-items: center;
      background: var(--accent); color: var(--accent-contrast); font-size: 0.6875rem; font-weight: 700; margin-top: 2px;
    }
    .actions { margin-top: 6px; }
    .action {
      display: inline-flex; align-items: center; gap: 6px; padding: 4px 8px; border-radius: 6px;
      border: 0; background: transparent; color: var(--text-muted); font: inherit; font-size: 0.8125rem; cursor: pointer;
    }
    .action:hover { background: var(--surface-hover); color: var(--text); }
  `,
})
export class ChatMessageView {
  private readonly notifications = inject(NotificationService);

  readonly message = input.required<ChatMessage>();
  protected readonly isUser = computed(() => this.message().role === 'user');
  protected readonly copied = signal(false);

  protected async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.message().content);
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    } catch {
      this.notifications.error('Could not copy to the clipboard.');
    }
  }
}
