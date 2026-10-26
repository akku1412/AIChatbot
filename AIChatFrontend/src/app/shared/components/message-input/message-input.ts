import { HttpEventType } from '@angular/common/http';
import {
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  untracked,
  inject,
  input,
  model,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { Subscription } from 'rxjs';

import { errorMessage } from '../../../core/http/api-error';
import {
  AttachmentInfo,
  DOC_NOT_SUPPORTED_TEXT,
  MAX_MESSAGE_LENGTH,
  SUPPORTED_FILES_TEXT,
  UploadedFile,
} from '../../../core/models/chat.models';
import { FileApiService, isProcessing, processingLabel } from '../../../core/services/file-api.service';
import { ImagePreviewService, createObjectUrl, revokeObjectUrl } from '../../../core/services/image-preview.service';
import { ModelService } from '../../../core/services/model.service';
import { MAX_RECORDING_SECONDS, VoiceInputError, VoiceRecorderService } from '../../../core/services/voice-recorder.service';
import { AttachmentChip, ChipState, formatFileSize } from '../attachment-chip/attachment-chip';
import { NotificationService } from '../toast/notification.service';

/** What the composer hands to the chat when the user sends. */
export interface ComposerSubmit {
  text: string;
  attachments: AttachmentInfo[];
  /** "Search the web" was on for this message. */
  webSearch: boolean;
}

interface PendingAttachment {
  id: number;
  file: File;
  state: ChipState;
  progress: number;
  error: string | null;
  /** While the server reads the file: its current step, e.g. "Reading scanned pages… 3/12". */
  detail: string | null;
  /** Images: a blob URL of the local file for the thumbnail (owned by ImagePreviewService once uploaded). */
  previewUrl: string | null;
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
    '(document:keydown.escape)': 'onEscape()',
  },
  templateUrl: './message-input.html',
  styleUrl: './message-input.css',
})
export class MessageInput {
  private readonly fileApi = inject(FileApiService);
  private readonly previews = inject(ImagePreviewService);
  private readonly models = inject(ModelService);
  private readonly notifications = inject(NotificationService);

  /** Two-way bound draft text, so the parent can restore it after a failed send. */
  readonly text = model('');
  readonly disabled = input(false);
  /** True while a reply is being generated: typing is allowed, sending is not; the send button becomes Stop. */
  readonly busy = input(false);
  /** Stop was pressed and the server is finishing up. */
  readonly stopping = input(false);
  readonly placeholder = input('Message AIChat');
  readonly send = output<ComposerSubmit>();
  readonly stop = output<void>();

  protected readonly maxLength = MAX_MESSAGE_LENGTH;
  protected readonly attachments = signal<PendingAttachment[]>([]);
  protected readonly dragging = signal(false);
  protected readonly limits = this.fileApi.limits;
  protected readonly accept = computed(() => this.limits().allowedExtensions.join(','));
  /** Shown only when the server has a search API key. */
  protected readonly webSearchAvailable = this.models.webSearchAvailable;
  /** "Search the web" for the next message (off again after it is sent). */
  protected readonly webSearch = signal(false);
  protected readonly recorder = inject(VoiceRecorderService);
  /** Shown when the server has speech-to-text and this browser can record. */
  protected readonly voiceAvailable = computed(() => this.models.voiceInputAvailable() && this.recorder.supported);
  protected readonly recordingTime = computed(() => {
    const s = this.recorder.seconds();
    return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
  });

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
      this.recorder.state() === 'idle' &&
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
    this.models.load();

    // Keep the height in sync when the text changes from outside (cleared or restored).
    effect(() => {
      this.text();
      queueMicrotask(() => this.autosize());
    });

    // Long recordings finish by themselves (and are transcribed like a normal stop).
    effect(() => {
      if (this.recorder.state() === 'recording' && this.recorder.seconds() >= MAX_RECORDING_SECONDS) {
        untracked(() => this.finishRecording());
      }
    });

    // Leaving the page cancels uploads and recordings still in flight and frees thumbnails nobody else owns.
    inject(DestroyRef).onDestroy(() => {
      if (this.recorder.state() === 'recording' || this.recorder.state() === 'starting') {
        this.recorder.cancel();
      }
      this.attachments().forEach((a) => {
        a.subscription?.unsubscribe();
        if (!a.uploaded || a.state !== 'ready') {
          revokeObjectUrl(a.previewUrl);
        }
      });
    });
  }

  /** Mic button: start recording (asks for permission the first time). */
  protected async startRecording(): Promise<void> {
    try {
      await this.recorder.start();
    } catch (err) {
      this.notifications.error(err instanceof VoiceInputError ? err.message : "Couldn't start the microphone.");
    }
  }

  /** Stop button (or the time limit): transcribe and add the words to the message, ready to edit or send. */
  protected async finishRecording(): Promise<void> {
    try {
      const words = (await this.recorder.stopAndTranscribe()).trim();
      const current = this.text().trimEnd();
      this.text.set(current ? `${current} ${words}` : words);
      this.focus();
    } catch (err) {
      this.notifications.error(err instanceof VoiceInputError ? err.message : "Couldn't transcribe the recording.");
    }
  }

  /** Esc or the discard button while recording: nothing is sent. */
  protected cancelRecording(): void {
    this.recorder.cancel();
  }

  protected onEscape(): void {
    if (this.recorder.state() === 'recording') {
      this.cancelRecording();
    }
  }

  focus(): void {
    this.box().nativeElement.focus();
  }

  /**
   * Called by the chat after a successful send: the files now belong to the message, so keep them on the server.
   * Web search is per message, so it switches off again too.
   */
  clearAttachments(): void {
    this.attachments.set([]);
    this.webSearch.set(false);
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
      this.previews.forget(attachment.uploaded.fileId);
    }
    revokeObjectUrl(attachment.previewUrl);
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
        isImage: f.isImage ?? false,
      })),
      webSearch: this.webSearch() && this.webSearchAvailable(),
    });
  }

  private addFiles(files: File[]): void {
    const { maxFileSizeBytes, maxFilesPerMessage, allowedExtensions, maxImageSizeBytes, imageExtensions } = this.limits();
    const room = maxFilesPerMessage - this.attachments().filter((a) => a.state !== 'error').length;

    if (files.length > room) {
      this.notifications.error(`You can attach up to ${maxFilesPerMessage} files to one message.`);
      files = files.slice(0, Math.max(room, 0));
    }

    for (const file of files) {
      const extension = file.name.includes('.') ? file.name.slice(file.name.lastIndexOf('.')).toLowerCase() : '';
      if (extension === '.doc') {
        this.notifications.error(`"${file.name}": ${DOC_NOT_SUPPORTED_TEXT}`);
        continue;
      }
      if (!allowedExtensions.includes(extension)) {
        this.notifications.error(`"${file.name}" isn't supported. Supported files: ${SUPPORTED_FILES_TEXT}.`);
        continue;
      }
      if (file.size === 0) {
        this.notifications.error(`"${file.name}" is empty.`);
        continue;
      }
      const isImage = imageExtensions.includes(extension);
      const limit = isImage ? maxImageSizeBytes : maxFileSizeBytes;
      if (file.size > limit) {
        this.notifications.error(
          `"${file.name}" is ${formatFileSize(file.size)}; the limit${isImage ? ' for images' : ''} is ${formatFileSize(limit)}.`,
        );
        continue;
      }
      this.startUpload(file, isImage);
    }
  }

  private startUpload(file: File, isImage: boolean): void {
    const id = this.nextId++;
    const previewUrl = isImage ? createObjectUrl(file) : null;
    this.attachments.update((list) => [
      ...list,
      { id, file, state: 'uploading', progress: 0, error: null, detail: null, previewUrl, uploaded: null, subscription: null },
    ]);

    const subscription = this.fileApi.upload(file).subscribe({
      next: (event) => {
        if (event.type === HttpEventType.UploadProgress) {
          const progress = event.total ? Math.round((event.loaded / event.total) * 100) : 0;
          // All bytes sent: the server is now storing and checking the document.
          this.patch(id, progress >= 100 ? { state: 'processing', progress: 0 } : { progress });
        } else if (event.type === HttpEventType.Response && event.body) {
          const uploaded = event.body;
          if (isProcessing(uploaded)) {
            // Stored; the server reads it in the background. Keep "uploaded" so removing it deletes it.
            this.patch(id, { state: 'processing', ...this.processingState(uploaded), uploaded });
            this.watchProcessing(id, uploaded.fileId);
          } else {
            this.finish(id, uploaded);
          }
        }
      },
      error: (err: unknown) => this.patch(id, { state: 'error', error: errorMessage(err), subscription: null }),
    });
    // A synchronous "processing" response may already have replaced the subscription with the watcher.
    if (this.attachments().find((a) => a.id === id)?.subscription == null) {
      this.patch(id, { subscription });
    }
  }

  private watchProcessing(id: number, fileId: string): void {
    const subscription = this.fileApi.watch(fileId).subscribe({
      next: (file) =>
        isProcessing(file) ? this.patch(id, this.processingState(file)) : this.finish(id, file),
      error: (err: unknown) => this.patch(id, { state: 'error', error: errorMessage(err), subscription: null }),
    });
    this.patch(id, { subscription });
  }

  private finish(id: number, file: UploadedFile): void {
    const previewUrl = this.attachments().find((a) => a.id === id)?.previewUrl;
    if (file.status === 'completed' && previewUrl) {
      // The sent message shows the same thumbnail without downloading the image again.
      this.previews.adopt(file.fileId, previewUrl);
    }
    this.patch(id, file.status === 'completed'
      ? { state: 'ready', uploaded: file, detail: null, subscription: null }
      : { state: 'error', error: file.statusMessage ?? 'This file could not be read.', detail: null, subscription: null });
  }

  /** Step label and, when the server counts (e.g. scanned pages), a percentage for the chip's bar. */
  private processingState(file: UploadedFile): Pick<PendingAttachment, 'detail' | 'progress'> {
    const total = file.progressTotal ?? 0;
    return {
      detail: processingLabel(file),
      progress: total > 0 ? Math.round(((file.progressCurrent ?? 0) / total) * 100) : 0,
    };
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
