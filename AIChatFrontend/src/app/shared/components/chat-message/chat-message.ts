import { Component, ElementRef, computed, inject, input, output, signal } from '@angular/core';
import { finalize } from 'rxjs';

import { errorMessage } from '../../../core/http/api-error';
import { AttachmentInfo, ChatMessage, ExportFormat } from '../../../core/models/chat.models';
import { ChatApiService } from '../../../core/services/chat-api.service';
import { safeFileName, saveResponse } from '../../../core/services/download';
import { FileApiService } from '../../../core/services/file-api.service';
import { AppLogo } from '../app-logo/app-logo';
import { AttachmentChip } from '../attachment-chip/attachment-chip';
import { MarkdownView } from '../markdown/markdown';
import { NotificationService } from '../toast/notification.service';

interface ExportOption {
  format: ExportFormat;
  label: string;
  hint: string;
}

/**
 * One message. User messages are right-aligned plain-text bubbles with their attached files (downloadable);
 * assistant messages are full-width sanitized Markdown with Copy and a Download menu (TXT, PDF, DOCX, XLSX),
 * whose files are generated on the server.
 */
@Component({
  selector: 'app-chat-message',
  imports: [AppLogo, AttachmentChip, MarkdownView],
  host: {
    '(document:click)': 'onDocumentClick($event)',
    '(document:keydown.escape)': 'menuOpen.set(false)',
  },
  template: `
    <article class="message" [class.user]="isUser()" [class.assistant]="!isUser()" [attr.aria-label]="isUser() ? 'You said' : 'AIChat replied'">
      @if (!isUser()) {
        <app-logo class="avatar" [size]="30" />
      }
      <div class="body">
        @if (isUser()) {
          @if (message().attachments.length) {
            <div class="files" role="list" aria-label="Attached files">
              @for (file of message().attachments; track file.fileId) {
                <app-attachment-chip
                  role="listitem"
                  state="sent"
                  [fileName]="file.fileName"
                  [fileSize]="file.fileSize"
                  [downloadable]="message().messageId > 0"
                  [downloading]="downloadingFileId() === file.fileId"
                  (download)="downloadFile(file)"
                />
              }
            </div>
          }
          @if (message().content) {
            <div class="content">{{ message().content }}</div>
          }
        } @else {
          <app-markdown [content]="message().content" [streaming]="streaming()" />
        }
        @if (!isUser() && !streaming()) {
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

            @if (canRegenerate()) {
              <button type="button" class="action" (click)="regenerate.emit()" aria-label="Regenerate response" title="Regenerate response">
                <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M20 11a8 8 0 0 0-14.3-4.9L4 8M4 4v4h4M4 13a8 8 0 0 0 14.3 4.9L20 16M20 20v-4h-4" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>
                <span>Regenerate</span>
              </button>
            }

            <div class="export">
              <button
                type="button"
                class="action"
                aria-haspopup="menu"
                [attr.aria-expanded]="menuOpen()"
                [attr.aria-busy]="!!exporting()"
                [disabled]="!!exporting()"
                aria-label="Download response"
                (click)="toggleMenu($event)"
              >
                @if (exporting(); as format) {
                  <span class="spinner" aria-hidden="true"></span>
                  <span>Preparing {{ format.toUpperCase() }}…</span>
                } @else {
                  <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M12 4v11M7 10l5 5 5-5M5 20h14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>
                  <span>Download</span>
                  <svg viewBox="0 0 24 24" width="14" height="14" aria-hidden="true"><path d="M6 9l6 6 6-6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>
                }
              </button>
              @if (menuOpen()) {
                <div class="menu" role="menu" aria-label="Download response as">
                  @for (option of exportOptions; track option.format) {
                    <button type="button" role="menuitem" (click)="exportAs(option.format)">
                      <span class="badge" [class]="option.format" aria-hidden="true">{{ option.format.toUpperCase() }}</span>
                      <span class="text">
                        <span class="label">{{ option.label }}</span>
                        <span class="hint">{{ option.hint }}</span>
                      </span>
                    </button>
                  }
                </div>
              }
            </div>
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
      background: color-mix(in srgb, var(--accent) 12%, var(--surface)); padding: 10px 16px;
      border: 1px solid color-mix(in srgb, var(--accent) 18%, transparent); border-radius: 18px 18px 6px 18px;
    }
    .user .body { display: flex; flex-direction: column; align-items: flex-end; gap: 6px; }
    .files { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 6px; max-width: 100%; }
    .assistant .body { flex: 1; min-width: 0; }
    .content { white-space: pre-wrap; overflow-wrap: anywhere; line-height: 1.6; }
    .avatar { margin-top: 2px; }
    .actions { margin-top: 6px; display: flex; align-items: center; gap: 2px; }
    .action {
      display: inline-flex; align-items: center; gap: 6px; padding: 4px 8px; border-radius: 6px;
      border: 0; background: transparent; color: var(--text-muted); font: inherit; font-size: 0.8125rem; cursor: pointer;
    }
    .action:hover:not(:disabled) { background: var(--surface-hover); color: var(--text); }
    .action:disabled { cursor: progress; color: var(--accent); }
    .export { position: relative; }
    /* Opens upwards: the newest reply sits right above the composer. */
    .menu {
      position: absolute; bottom: calc(100% + 6px); left: 0; z-index: 20; min-width: 248px; padding: 6px;
      border: 1px solid var(--border); border-radius: 12px; background: var(--surface-raised); box-shadow: var(--shadow-md);
    }
    .menu button {
      display: flex; align-items: center; gap: 10px; width: 100%; padding: 7px 8px; border: 0; border-radius: 8px;
      background: transparent; color: var(--text); font: inherit; text-align: left; cursor: pointer;
    }
    .menu button:hover, .menu button:focus-visible { background: var(--surface-hover); outline: none; }
    .badge {
      flex: none; width: 38px; height: 24px; border-radius: 6px; display: grid; place-items: center;
      color: #fff; font-size: 0.625rem; font-weight: 800; letter-spacing: 0.02em;
    }
    .badge.txt { background: #64748b; }
    .badge.pdf { background: #dc2626; }
    .badge.docx { background: #2563eb; }
    .badge.xlsx { background: #16a34a; }
    .text { display: flex; flex-direction: column; min-width: 0; }
    .label { font-size: 0.875rem; font-weight: 500; }
    .hint { font-size: 0.75rem; color: var(--text-muted); }
    .spinner {
      width: 14px; height: 14px; border-radius: 50%; border: 2px solid color-mix(in srgb, var(--accent) 25%, transparent);
      border-top-color: var(--accent); animation: spin 0.8s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }
    @media (prefers-reduced-motion: reduce) { .spinner { animation-duration: 2.4s; } }
  `,
})
export class ChatMessageView {
  private readonly notifications = inject(NotificationService);
  private readonly chatApi = inject(ChatApiService);
  private readonly fileApi = inject(FileApiService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly message = input.required<ChatMessage>();
  /** Fallback for naming downloads if the server sends no file name. */
  readonly conversationTitle = input('');
  /** The reply is still arriving: no actions yet (it has no saved id). */
  readonly streaming = input(false);
  /** Only the latest reply can be regenerated. */
  readonly canRegenerate = input(false);
  readonly regenerate = output<void>();

  protected readonly isUser = computed(() => this.message().role === 'user');
  protected readonly copied = signal(false);
  protected readonly menuOpen = signal(false);
  /** The format being generated right now (one at a time per message). */
  protected readonly exporting = signal<ExportFormat | null>(null);
  protected readonly downloadingFileId = signal<string | null>(null);

  protected readonly exportOptions: ExportOption[] = [
    { format: 'txt', label: 'Download as TXT', hint: 'Plain text' },
    { format: 'pdf', label: 'Download as PDF', hint: 'Formatted document' },
    { format: 'docx', label: 'Download as DOCX', hint: 'Editable Word document' },
    { format: 'xlsx', label: 'Download as XLSX', hint: 'Tables as an Excel sheet' },
  ];

  protected toggleMenu(event: MouseEvent): void {
    event.stopPropagation();
    this.menuOpen.update((open) => !open);
  }

  protected onDocumentClick(event: MouseEvent): void {
    if (this.menuOpen() && !this.host.nativeElement.querySelector('.export')?.contains(event.target as Node)) {
      this.menuOpen.set(false);
    }
  }

  protected exportAs(format: ExportFormat): void {
    this.menuOpen.set(false);
    if (this.exporting()) {
      return;
    }
    this.exporting.set(format);
    const fallback = `AIChat reply - ${safeFileName(this.conversationTitle())}.${format}`;
    this.chatApi
      .exportMessage(this.message().messageId, format)
      .pipe(finalize(() => this.exporting.set(null)))
      .subscribe({
        next: (response) => saveResponse(response, fallback),
        error: (err: unknown) => this.notifications.error(`Couldn't create the ${format.toUpperCase()} file. ${errorMessage(err)}`),
      });
  }

  protected downloadFile(file: AttachmentInfo): void {
    if (this.downloadingFileId()) {
      return;
    }
    this.downloadingFileId.set(file.fileId);
    this.fileApi
      .download(file.fileId)
      .pipe(finalize(() => this.downloadingFileId.set(null)))
      .subscribe({
        next: (response) => saveResponse(response, file.fileName),
        error: (err: unknown) => this.notifications.error(`Couldn't download "${file.fileName}". ${errorMessage(err)}`),
      });
  }

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
