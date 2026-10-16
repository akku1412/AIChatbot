import { HttpEventType } from '@angular/common/http';
import {
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { Subscription } from 'rxjs';

import { errorMessage } from '../../../core/http/api-error';
import { AttachmentInfo, MAX_MESSAGE_LENGTH, UploadedFile } from '../../../core/models/chat.models';
import { FileApiService } from '../../../core/services/file-api.service';
import { AttachmentChip, ChipState, formatFileSize } from '../attachment-chip/attachment-chip';
import { NotificationService } from '../toast/notification.service';

/** What the composer hands to the chat when the user sends. */
export interface ComposerSubmit {
  text: string;
  attachments: AttachmentInfo[];
}

interface PendingAttachment {
  id: number;
  file: File;
  state: ChipState;
  progress: number;
  error: string | null;
  uploaded: UploadedFile | null;
  subscription: Subscription | null;
}

/**
 * Multiline composer with file attachments.
 * - Enter sends, Shift+Enter inserts a new line; Enter during IME composition never sends.
 * - Files (paperclip, or drag & drop onto the composer) are checked locally, then uploaded at once with
 *   progress; the server extracts their text. Removing a file cancels its upload or deletes it on the server.
 * - Sending waits until every attached file is ready.
 */
@Component({
  selector: 'app-message-input',
  imports: [AttachmentChip],
  host: {
    '(dragenter)': 'onDragOver($event)',
    '(dragover)': 'onDragOver($event)',
    '(dragleave)': 'onDragLeave($event)',
    '(drop)': 'onDrop($event)',
  },
  templateUrl: './message-input.html',
  styleUrl: './message-input.css',
})
export class MessageInput {
  private readonly fileApi = inject(FileApiService);
  private readonly notifications = inject(NotificationService);

  /** Two-way bound draft text, so the parent can restore it after a failed send. */
  readonly text = model('');
  readonly disabled = input(false);
  /** True while a reply is being generated: typing is allowed, sending is not. */
  readonly busy = input(false);
  readonly placeholder = input('Message AIChat');
  readonly send = output<ComposerSubmit>();

  protected readonly maxLength = MAX_MESSAGE_LENGTH;
  protected readonly attachments = signal<PendingAttachment[]>([]);
  protected readonly dragging = signal(false);
  protected readonly limits = this.fileApi.limits;
  protected readonly accept = computed(() => this.limits().allowedExtensions.join(','));

  protected readonly remaining = computed(() => MAX_MESSAGE_LENGTH - this.text().length);
  protected readonly uploading = computed(() =>
    this.attachments().some((a) => a.state === 'uploading' || a.state === 'processing'),
  );
  protected readonly hasErrors = computed(() => this.attachments().some((a) => a.state === 'error'));
  protected readonly readyFiles = computed(() =>
    this.attachments().filter((a) => a.state === 'ready' && a.uploaded).map((a) => a.uploaded!),
  );
  protected readonly canSend = computed(
    () =>
      !this.disabled() &&
      !this.busy() &&
      !this.uploading() &&
      !this.hasErrors() &&
      (this.text().trim().length > 0 || this.readyFiles().length > 0) &&
      this.remaining() >= 0,
  );

  private readonly box = viewChild.required<ElementRef<HTMLTextAreaElement>>('box');
  private readonly picker = viewChild.required<ElementRef<HTMLInputElement>>('picker');
  private nextId = 1;

  constructor() {
    this.fileApi.loadLimits();

    // Keep the height in sync when the text changes from outside (cleared or restored).
    effect(() => {
      this.text();
      queueMicrotask(() => this.autosize());
    });

    // Leaving the page cancels uploads still in flight.
    inject(DestroyRef).onDestroy(() => this.attachments().forEach((a) => a.subscription?.unsubscribe()));
  }

  focus(): void {
    this.box().nativeElement.focus();
  }

  /** Called by the chat after a successful send: the files now belong to the message, so keep them on the server. */
  clearAttachments(): void {
    this.attachments.set([]);
  }

  protected openPicker(): void {
    this.picker().nativeElement.click();
  }

  protected onPicked(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.addFiles(Array.from(input.files ?? []));
    input.value = ''; // allow picking the same file again
  }

  protected onDragOver(event: DragEvent): void {
    if (this.disabled() || !event.dataTransfer?.types.includes('Files')) {
      return;
    }
    event.preventDefault();
    event.dataTransfer.dropEffect = 'copy';
    this.dragging.set(true);
  }

  protected onDragLeave(event: DragEvent): void {
    // Only when the pointer really leaves the composer (not when moving over a child element).
    const host = event.currentTarget as HTMLElement;
    if (!event.relatedTarget || !host.contains(event.relatedTarget as Node)) {
      this.dragging.set(false);
    }
  }

  protected onDrop(event: DragEvent): void {
    if (!event.dataTransfer?.files.length) {
      return;
    }
    event.preventDefault();
    this.dragging.set(false);
    if (!this.disabled()) {
      this.addFiles(Array.from(event.dataTransfer.files));
    }
  }

  protected remove(id: number): void {
    const attachment = this.attachments().find((a) => a.id === id);
    if (!attachment) {
      return;
    }
    // Cancels the HTTP upload if it is still running.
    attachment.subscription?.unsubscribe();
    if (attachment.uploaded) {
      // Uploaded but never sent: don't leave an orphan on the server.
      this.fileApi.delete(attachment.uploaded.fileId).subscribe({ error: () => undefined });
    }
    this.attachments.update((list) => list.filter((a) => a.id !== id));
  }

  protected onInput(event: Event): void {
    this.text.set((event.target as HTMLTextAreaElement).value);
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      this.submit();
    }
  }

  protected submit(): void {
    if (!this.canSend()) {
      return;
    }
    this.send.emit({
      text: this.text().trim(),
      attachments: this.readyFiles().map((f) => ({
        fileId: f.fileId,
        fileName: f.fileName,
        fileExtension: f.fileExtension,
        fileSize: f.fileSize,
      })),
    });
  }

  private addFiles(files: File[]): void {
    const { maxFileSizeBytes, maxFilesPerMessage, allowedExtensions } = this.limits();
    const room = maxFilesPerMessage - this.attachments().filter((a) => a.state !== 'error').length;

    if (files.length > room) {
      this.notifications.error(`You can attach up to ${maxFilesPerMessage} files to one message.`);
      files = files.slice(0, Math.max(room, 0));
    }

    for (const file of files) {
      const extension = file.name.includes('.') ? file.name.slice(file.name.lastIndexOf('.')).toLowerCase() : '';
      if (!allowedExtensions.includes(extension)) {
        this.notifications.error(`"${file.name}" isn't supported. Attach PDF, Word (.docx), Excel (.xlsx, .xls) or text (.txt) files.`);
        continue;
      }
      if (file.size === 0) {
        this.notifications.error(`"${file.name}" is empty.`);
        continue;
      }
      if (file.size > maxFileSizeBytes) {
        this.notifications.error(`"${file.name}" is ${formatFileSize(file.size)}; the limit is ${formatFileSize(maxFileSizeBytes)}.`);
        continue;
      }
      this.startUpload(file);
    }
  }

  private startUpload(file: File): void {
    const id = this.nextId++;
    this.attachments.update((list) => [
      ...list,
      { id, file, state: 'uploading', progress: 0, error: null, uploaded: null, subscription: null },
    ]);

    const subscription = this.fileApi.upload(file).subscribe({
      next: (event) => {
        if (event.type === HttpEventType.UploadProgress) {
          const progress = event.total ? Math.round((event.loaded / event.total) * 100) : 0;
          // All bytes sent: the server is now reading the document.
          this.patch(id, progress >= 100 ? { state: 'processing', progress: 100 } : { progress });
        } else if (event.type === HttpEventType.Response && event.body) {
          const uploaded = event.body;
          this.patch(id, uploaded.status === 'completed'
            ? { state: 'ready', uploaded, subscription: null }
            : { state: 'error', error: uploaded.statusMessage ?? 'This file could not be read.', subscription: null });
        }
      },
      error: (err: unknown) => this.patch(id, { state: 'error', error: errorMessage(err), subscription: null }),
    });
    this.patch(id, { subscription });
  }

  private patch(id: number, changes: Partial<PendingAttachment>): void {
    this.attachments.update((list) => list.map((a) => (a.id === id ? { ...a, ...changes } : a)));
  }

  private autosize(): void {
    const el = this.box().nativeElement;
    el.style.height = 'auto';
    el.style.height = `${Math.min(el.scrollHeight, 200)}px`;
  }
}
