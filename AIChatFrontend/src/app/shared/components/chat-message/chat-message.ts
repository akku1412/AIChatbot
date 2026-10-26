import { Component, ElementRef, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { finalize } from 'rxjs';

import { errorMessage } from '../../../core/http/api-error';
import { AttachmentInfo, ChatMessage, ExportFormat, MAX_MESSAGE_LENGTH, MessageSource } from '../../../core/models/chat.models';
import { ChatApiService } from '../../../core/services/chat-api.service';
import { safeFileName, saveResponse } from '../../../core/services/download';
import { FileApiService } from '../../../core/services/file-api.service';
import { ImagePreviewService } from '../../../core/services/image-preview.service';
import { SourcePreviewService } from '../../../core/services/source-preview.service';
import { AppLogo } from '../app-logo/app-logo';
import { AttachmentChip } from '../attachment-chip/attachment-chip';
import { MarkdownView } from '../markdown/markdown';
import { NotificationService } from '../toast/notification.service';
import { VersionSwitcher } from '../version-switcher/version-switcher';

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
  imports: [AppLogo, AttachmentChip, MarkdownView, VersionSwitcher],
  host: {
    '(document:click)': 'onDocumentClick($event)',
    '(document:keydown.escape)': 'menuOpen.set(false); viewing.set(null)',
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
                @if (file.isImage) {
                  <button type="button" class="image-file" role="listitem" [attr.aria-label]="'View ' + file.fileName"
                          [title]="file.fileName" (click)="viewImage(file)">
                    @if (imageUrls().get(file.fileId)?.(); as src) {
                      <img [src]="src" alt="" />
                    } @else {
                      <span class="image-loading" aria-hidden="true">IMG</span>
                    }
                  </button>
                } @else {
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
              }
            </div>
          }
          @if (viewing(); as file) {
            <div class="lightbox" role="dialog" aria-modal="true" [attr.aria-label]="file.fileName" (click)="viewing.set(null)">
              @if (imageUrls().get(file.fileId)?.(); as src) {
                <img [src]="src" [alt]="file.fileName" (click)="$event.stopPropagation()" />
              }
              <div class="lightbox-bar" (click)="$event.stopPropagation()">
                <span class="lightbox-name">{{ file.fileName }}</span>
                @if (message().messageId > 0) {
                  <button type="button" [disabled]="downloadingFileId() === file.fileId" (click)="downloadFile(file)">Download</button>
                }
                <button #closeViewer type="button" (click)="viewing.set(null)">Close</button>
              </div>
            </div>
          }
          @if (editing()) {
            <div class="editor">
              <textarea
                #editBox
                rows="3"
                aria-label="Edit your message"
                [value]="draft()"
                [attr.maxlength]="maxLength"
                (input)="draft.set($any($event.target).value)"
                (keydown.escape)="cancelEdit()"
                (keydown.control.enter)="submitEdit()"
                (keydown.meta.enter)="submitEdit()"
              ></textarea>
              <div class="editor-actions">
                <button type="button" class="ghost" (click)="cancelEdit()">Cancel</button>
                <button type="button" class="primary" [disabled]="!canSubmitEdit()" (click)="submitEdit()">Save &amp; submit</button>
              </div>
            </div>
          } @else {
            @if (message().content) {
              <div class="content">{{ message().content }}</div>
            }
            @if (message().messageId > 0 && (canEdit() || message().versionCount > 1)) {
              <div class="user-actions">
                @if (message().versionCount > 1) {
                  <app-version-switcher
                    [current]="message().versionNumber"
                    [count]="message().versionCount"
                    [disabled]="!canSwitch()"
                    (select)="switchVersion.emit($event)"
                  />
                }
                @if (canEdit()) {
                  <button type="button" class="action icon-only" aria-label="Edit message" title="Edit message" (click)="startEdit()">
                    <svg viewBox="0 0 24 24" width="15" height="15" aria-hidden="true"><path d="M4 20h4L19 9l-4-4L4 16z" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linejoin="round"/></svg>
                  </button>
                }
              </div>
            }
          }
        } @else {
          <app-markdown [content]="message().content" [streaming]="streaming()" [sources]="sources()" (cite)="openSource($event)" />
          @if (message().isTruncated && !streaming()) {
            <p class="stopped">(stopped)</p>
          }
          @if (sources().length && !streaming()) {
            <div class="sources" role="group" aria-label="Sources">
              <span class="sources-label">Sources</span>
              @for (source of sources(); track source.chunkId) {
                <button type="button" class="source" [title]="source.fileName + ', ' + source.location"
                        [attr.aria-label]="'Source: ' + source.fileName + ', ' + source.location" (click)="openSource(source)">
                  <span class="source-file">{{ source.fileName }}</span>
                  <span class="source-where">{{ source.location }}</span>
                </button>
              }
            </div>
          }
        }
        @if (!isUser() && !streaming()) {
          <div class="actions">
            @if (message().versionCount > 1) {
              <app-version-switcher
                [current]="message().versionNumber"
                [count]="message().versionCount"
                [disabled]="!canSwitch()"
                (select)="switchVersion.emit($event)"
              />
            }
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
    .sources { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; margin-top: 10px; }
    .sources-label { font-size: 0.75rem; font-weight: 600; color: var(--text-muted); margin-right: 2px; }
    .source {
      display: inline-flex; gap: 6px; max-width: 100%; padding: 3px 10px; border: 1px solid var(--border);
      border-radius: 999px; background: var(--surface); color: var(--text); font: inherit; font-size: 0.75rem; cursor: pointer;
    }
    .source:hover, .source:focus-visible { border-color: var(--accent); }
    .source-file { font-weight: 600; max-width: 180px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .source-where { color: var(--text-muted); white-space: nowrap; }
    .image-file {
      width: 160px; height: 160px; padding: 0; border: 1px solid var(--border); border-radius: 12px; overflow: hidden;
      background: var(--surface-raised); cursor: zoom-in; display: grid; place-items: center;
    }
    .image-file img { width: 100%; height: 100%; object-fit: cover; display: block; }
    .image-file:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
    .image-loading { font-size: 0.75rem; font-weight: 700; color: var(--text-muted); }
    .lightbox {
      position: fixed; inset: 0; z-index: 1000; display: flex; flex-direction: column; align-items: center;
      justify-content: center; gap: 12px; padding: 16px; background: rgb(0 0 0 / 0.8);
    }
    .lightbox img { max-width: 100%; max-height: calc(100vh - 96px); object-fit: contain; border-radius: 8px; }
    .lightbox-bar { display: flex; align-items: center; gap: 8px; max-width: 100%; color: #fff; }
    .lightbox-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 0.875rem; }
    .lightbox-bar button {
      flex: none; padding: 6px 12px; border: 1px solid rgb(255 255 255 / 0.4); border-radius: 8px;
      background: transparent; color: #fff; cursor: pointer;
    }
    .lightbox-bar button:hover:not(:disabled) { background: rgb(255 255 255 / 0.12); }
    .assistant .body { flex: 1; min-width: 0; }
    .content { white-space: pre-wrap; overflow-wrap: anywhere; line-height: 1.6; }
    .avatar { margin-top: 2px; }
    .actions { margin-top: 6px; display: flex; align-items: center; gap: 2px; flex-wrap: wrap; }
    .user-actions { display: flex; align-items: center; gap: 2px; }
    .action.icon-only { padding: 5px; }
    /* The edit pencil appears on hover or keyboard focus (touch screens always show it); version arrows stay visible. */
    .user-actions .icon-only { opacity: 0; transition: opacity 120ms; }
    .message:hover .user-actions .icon-only, .user-actions .icon-only:focus-visible { opacity: 1; }
    @media (hover: none) { .user-actions .icon-only { opacity: 1; } }
    .stopped { margin: 4px 0 0; color: var(--text-muted); font-size: 0.8125rem; font-style: italic; }
    .editor { width: min(560px, 100%); display: flex; flex-direction: column; gap: 8px; }
    .editor textarea {
      width: 100%; min-height: 84px; max-height: 320px; resize: vertical; padding: 10px 14px;
      border: 1px solid color-mix(in srgb, var(--accent) 45%, var(--border)); border-radius: 14px;
      background: var(--surface-raised); color: var(--text); font: inherit; line-height: 1.5;
    }
    .editor textarea:focus { outline: none; box-shadow: 0 0 0 3px color-mix(in srgb, var(--accent) 15%, transparent); }
    .editor-actions { display: flex; justify-content: flex-end; gap: 8px; }
    .editor-actions button {
      padding: 6px 14px; border-radius: 999px; font: inherit; font-size: 0.875rem; font-weight: 600; cursor: pointer;
    }
    .editor-actions .ghost { border: 1px solid var(--border); background: transparent; color: var(--text); }
    .editor-actions .primary { border: 0; background: var(--text); color: var(--bg); }
    .editor-actions .primary:disabled { opacity: 0.4; cursor: not-allowed; }
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
  private readonly previews = inject(ImagePreviewService);
  private readonly sourcePreview = inject(SourcePreviewService);

  /** Passages this reply cited (the server keeps only real, owned ones). */
  protected readonly sources = computed(() => this.message().sources ?? []);

  protected openSource(source: MessageSource): void {
    this.sourcePreview.show(source);
  }

  /** Thumbnail URLs of this message's images (fetched once, shared with every view of the same file). */
  protected readonly imageUrls = computed(
    () => new Map(this.message().attachments.filter((f) => f.isImage).map((f) => [f.fileId, this.previews.url(f.fileId)])),
  );
  /** The image open in the full-size viewer. */
  protected readonly viewing = signal<AttachmentInfo | null>(null);
  private readonly closeViewer = viewChild<ElementRef<HTMLButtonElement>>('closeViewer');

  protected viewImage(file: AttachmentInfo): void {
    this.viewing.set(file);
    setTimeout(() => this.closeViewer()?.nativeElement.focus()); // after the viewer renders
  }
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly message = input.required<ChatMessage>();
  /** Fallback for naming downloads if the server sends no file name. */
  readonly conversationTitle = input('');
  /** The reply is still arriving: no actions yet (it has no saved id). */
  readonly streaming = input(false);
  /** Only the latest reply can be regenerated. */
  readonly canRegenerate = input(false);
  /** User messages can be edited and resent (not while a reply is streaming). */
  readonly canEdit = input(false);
  /** The version arrows work (not while a reply is streaming). */
  readonly canSwitch = input(true);
  readonly regenerate = output<void>();
  /** Edit and resend with this new text. */
  readonly edit = output<string>();
  /** Show this version (1-based) of the message. */
  readonly switchVersion = output<number>();

  protected readonly maxLength = MAX_MESSAGE_LENGTH;
  protected readonly editing = signal(false);
  protected readonly draft = signal('');
  protected readonly canSubmitEdit = computed(() => {
    const text = this.draft().trim();
    return text.length > 0 && text.length <= MAX_MESSAGE_LENGTH && text !== this.message().content.trim();
  });
  private readonly editBox = viewChild<ElementRef<HTMLTextAreaElement>>('editBox');

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

  protected startEdit(): void {
    this.draft.set(this.message().content);
    this.editing.set(true);
    setTimeout(() => {
      const box = this.editBox()?.nativeElement;
      box?.focus();
      box?.setSelectionRange(box.value.length, box.value.length);
    });
  }

  protected cancelEdit(): void {
    this.editing.set(false);
  }

  protected submitEdit(): void {
    if (!this.canSubmitEdit()) {
      return;
    }
    this.editing.set(false);
    this.edit.emit(this.draft().trim());
  }

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
