import { HttpErrorResponse, HttpEventType } from '@angular/common/http';
import { Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Subscription, filter, switchMap } from 'rxjs';

import { errorMessage } from '../../core/http/api-error';
import {
  ConversationSummary,
  PROJECT_INSTRUCTIONS_MAX,
  PROJECT_MAX_FILES,
  PROJECT_NAME_MAX,
  ProjectDetail,
  UploadedFile,
} from '../../core/models/chat.models';
import { ChatStore } from '../../core/services/chat.store';
import { FileApiService, isProcessing, processingLabel } from '../../core/services/file-api.service';
import { ProjectService } from '../../core/services/project.service';
import { AttachmentChip, formatFileSize } from '../../shared/components/attachment-chip/attachment-chip';
import { ConfirmDialogService } from '../../shared/components/confirm-dialog/confirm-dialog.service';
import { LoadingSpinner } from '../../shared/components/loading-spinner/loading-spinner';
import { NotificationService } from '../../shared/components/toast/notification.service';

/**
 * /projects/:projectId — a project's name and instructions, its shared files (searched by every chat in the project)
 * and its chats. Deleting the project moves its chats back to the main list and keeps the files.
 */
@Component({
  selector: 'app-project-page',
  imports: [RouterLink, AttachmentChip, LoadingSpinner],
  templateUrl: './project-page.html',
  styleUrls: ['../profile/profile-card.css', './project-page.css'],
})
export class ProjectPage {
  private readonly projects = inject(ProjectService);
  protected readonly store = inject(ChatStore);
  private readonly fileApi = inject(FileApiService);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);
  private readonly confirmDialog = inject(ConfirmDialogService);

  /** Route param (bound by withComponentInputBinding). */
  readonly projectId = input.required<string>();

  protected readonly nameMax = PROJECT_NAME_MAX;
  protected readonly instructionsMax = PROJECT_INSTRUCTIONS_MAX;
  protected readonly maxFiles = PROJECT_MAX_FILES;
  protected readonly accept = computed(() => this.fileApi.limits().allowedExtensions.join(','));

  protected readonly id = computed(() => Number(this.projectId()));
  protected readonly detail = signal<ProjectDetail | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly name = signal('');
  protected readonly instructions = signal('');
  protected readonly saving = signal(false);
  /** Files being uploaded right now (by local name). */
  protected readonly uploading = signal<string[]>([]);

  protected readonly changed = computed(() => {
    const d = this.detail();
    return !!d && (this.name().trim() !== d.name || this.instructions().trim() !== (d.instructions ?? ''));
  });
  protected readonly canSave = computed(
    () => this.changed() && !this.saving() && this.name().trim().length > 0 && this.instructions().length <= this.instructionsMax,
  );
  protected readonly chats = computed<ConversationSummary[]>(() => this.store.projectConversations()[this.id()] ?? []);
  protected readonly full = computed(() => (this.detail()?.files.length ?? 0) + this.uploading().length >= this.maxFiles);

  private readonly watchers = new Subscription();

  constructor() {
    this.fileApi.loadLimits();
    inject(DestroyRef).onDestroy(() => this.watchers.unsubscribe());

    effect(() => {
      const id = this.id();
      untracked(() => {
        this.store.startNewChat(); // nothing "open" in the sidebar while on this page
        this.detail.set(null);
        this.loadError.set(null);
        if (!Number.isInteger(id) || id <= 0) {
          this.loadError.set("This project doesn't exist.");
          return;
        }
        this.reload();
        this.store.loadProjectConversations(id);
      });
    });
  }

  protected reload(): void {
    const id = this.id();
    this.projects.get(id).subscribe({
      next: (detail) => {
        if (detail.projectId !== this.id()) {
          return; // navigated to another project meanwhile
        }
        this.detail.set(detail);
        this.name.set(detail.name);
        this.instructions.set(detail.instructions ?? '');
      },
      error: (err: unknown) => this.loadError.set(errorMessage(err)),
    });
  }

  protected save(): void {
    if (!this.canSave()) {
      return;
    }
    this.saving.set(true);
    const instructions = this.instructions().trim();
    this.projects.update(this.id(), this.name().trim(), instructions || null).subscribe({
      next: (project) => {
        this.saving.set(false);
        this.detail.update((d) => (d ? { ...d, name: project.name, instructions: project.instructions } : d));
        this.name.set(project.name);
        this.instructions.set(project.instructions ?? '');
        this.notifications.success('Project saved. Instructions apply from the next message in its chats.');
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.notifications.error(errorMessage(err));
      },
    });
  }

  protected discard(): void {
    const d = this.detail();
    this.name.set(d?.name ?? '');
    this.instructions.set(d?.instructions ?? '');
  }

  protected onPicked(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    const room = this.maxFiles - (this.detail()?.files.length ?? 0) - this.uploading().length;
    if (files.length > room) {
      this.notifications.error(`A project can hold up to ${this.maxFiles} files.`);
    }
    files.slice(0, Math.max(room, 0)).forEach((file) => this.upload(file));
  }

  /** Uploads one file, shares it with the project at once, then refreshes when the server has finished reading it. */
  private upload(file: File): void {
    const { maxFileSizeBytes, maxImageSizeBytes, imageExtensions } = this.fileApi.limits();
    const extension = file.name.includes('.') ? file.name.slice(file.name.lastIndexOf('.')).toLowerCase() : '';
    const limit = imageExtensions.includes(extension) ? maxImageSizeBytes : maxFileSizeBytes;
    if (file.size > limit) {
      this.notifications.error(`"${file.name}" is ${formatFileSize(file.size)}; the limit is ${formatFileSize(limit)}.`);
      return;
    }

    const projectId = this.id();
    this.uploading.update((list) => [...list, file.name]);
    const done = () => this.uploading.update((list) => list.filter((name, i) => i !== list.indexOf(file.name)));

    this.watchers.add(
      this.fileApi
        .upload(file)
        .pipe(
          filter((event) => event.type === HttpEventType.Response),
          switchMap((event) => {
            const uploaded = (event as { body: UploadedFile }).body;
            if (uploaded.status === 'failed') {
              throw new Error(uploaded.statusMessage ?? `"${file.name}" couldn't be read.`);
            }
            if (isProcessing(uploaded)) {
              // Keep refreshing the list while the server reads it (OCR can take a while).
              this.watchers.add(this.fileApi.watch(uploaded.fileId).subscribe({ complete: () => this.reloadIf(projectId) }));
            }
            return this.projects.addFile(projectId, uploaded.fileId);
          }),
        )
        .subscribe({
          next: (detail) => {
            done();
            if (detail.projectId === this.id()) {
              this.detail.update((d) => (d ? { ...d, files: detail.files } : detail));
            }
          },
          error: (err: unknown) => {
            done();
            // A file the server couldn't read arrives as a plain Error (thrown above); HTTP failures carry ProblemDetails.
            const reason = err instanceof HttpErrorResponse || !(err instanceof Error) ? errorMessage(err) : err.message;
            this.notifications.error(`Couldn't add "${file.name}". ${reason}`);
          },
        }),
    );
  }

  protected removeFile(file: UploadedFile): void {
    this.projects.removeFile(this.id(), file.fileId).subscribe({
      next: (detail) => this.detail.update((d) => (d ? { ...d, files: detail.files } : detail)),
      error: (err: unknown) => this.notifications.error(`Couldn't remove "${file.fileName}". ${errorMessage(err)}`),
    });
  }

  /** How the file chip shows a project file: being read, failed, or ready (size). */
  protected chipState(file: UploadedFile): 'processing' | 'error' | 'sent' {
    return isProcessing(file) ? 'processing' : file.status === 'failed' ? 'error' : 'sent';
  }

  protected processingText(file: UploadedFile): string | null {
    return processingLabel(file);
  }

  protected newChat(): void {
    this.store.newChatIn(this.id());
    this.router.navigate(['/chat']);
  }

  protected removeChat(chat: ConversationSummary): void {
    this.store.moveToProject(chat, null).subscribe({
      next: () => this.notifications.success(`"${chat.title}" moved to your main chat list.`),
      error: (err: unknown) => this.notifications.error(`Couldn't move the chat. ${errorMessage(err)}`),
    });
  }

  protected async deleteProject(): Promise<void> {
    const d = this.detail();
    if (!d) {
      return;
    }
    const confirmed = await this.confirmDialog.confirm({
      title: 'Delete project?',
      message: `"${d.name}" will be deleted. Its chats move back to your main chat list and its files stay in your uploads.`,
      confirmText: 'Delete project',
      danger: true,
    });
    if (!confirmed) {
      return;
    }
    this.projects.delete(d.projectId).subscribe({
      next: () => {
        this.store.projectDeleted(d.projectId);
        this.notifications.success(`Project "${d.name}" deleted.`);
        this.router.navigate(['/chat']);
      },
      error: (err: unknown) => this.notifications.error(`Couldn't delete the project. ${errorMessage(err)}`),
    });
  }

  private reloadIf(projectId: number): void {
    if (this.id() === projectId) {
      this.reload();
    }
  }
}
