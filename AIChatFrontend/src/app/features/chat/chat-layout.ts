import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter, finalize, map } from 'rxjs';

import { AuthService } from '../../core/auth/auth.service';
import { errorMessage } from '../../core/http/api-error';
import { ConversationSummary } from '../../core/models/chat.models';
import { ChatApiService } from '../../core/services/chat-api.service';
import { ChatStore } from '../../core/services/chat.store';
import { fileNameFromDisposition, safeFileName, saveBlob } from '../../core/services/download';
import { AppHeader } from '../../shared/components/app-header/app-header';
import { ConfirmDialogService } from '../../shared/components/confirm-dialog/confirm-dialog.service';
import { ModelPicker } from '../../shared/components/model-picker/model-picker';
import { ArchiveRequest, PinRequest, RenameRequest, Sidebar } from '../../shared/components/sidebar/sidebar';
import { NotificationService } from '../../shared/components/toast/notification.service';

/** Signed-in shell: sidebar (a drawer on small screens) + header + the routed page (chat or profile). */
@Component({
  selector: 'app-chat-layout',
  imports: [RouterOutlet, Sidebar, AppHeader, ModelPicker],
  host: { '(document:keydown.escape)': 'closeDrawer()' },
  template: `
    <div class="layout" [class.drawer-open]="drawerOpen()">
      <aside class="side">
        <app-sidebar
          [conversations]="store.conversations()"
          [activeId]="store.activeConversationId()"
          [loading]="store.listLoading()"
          [error]="store.listError()"
          [hasMore]="store.hasMoreConversations()"
          [user]="auth.user()"
          [searchResults]="store.searchResults()"
          [searchLoading]="store.searchLoading()"
          [searchError]="store.searchError()"
          [archived]="store.archivedConversations()"
          [archivedLoading]="store.archivedLoading()"
          (search)="store.search($event)"
          (pin)="pin($event)"
          (archive)="archive($event)"
          (openArchived)="store.loadArchived()"
          (newChat)="newChat()"
          (selected)="closeDrawer()"
          (closed)="closeDrawer()"
          (loadMore)="store.loadNextConversationPage()"
          (retry)="store.loadConversations()"
          (rename)="rename($event)"
          (remove)="remove($event)"
          (exportChat)="exportChat($event.conversationId)"
          (logout)="logout()"
        />
      </aside>

      @if (drawerOpen()) {
        <div class="backdrop" (click)="closeDrawer()" aria-hidden="true"></div>
      }

      <section class="main">
        <app-header
          [title]="title()"
          [canExport]="canExport()"
          [exporting]="exporting()"
          (menu)="drawerOpen.set(true)"
          (newChat)="newChat()"
          (export)="exportChat(store.activeConversationId()!)"
        >
          @if (onChat()) {
            <app-model-picker header-start />
          }
        </app-header>
        <router-outlet />
      </section>
    </div>
  `,
  styleUrl: './chat-layout.css',
})
export class ChatLayout {
  protected readonly store = inject(ChatStore);
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly chatApi = inject(ChatApiService);

  protected readonly drawerOpen = signal(false);

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map((e) => e.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  protected readonly title = computed(() =>
    this.url().startsWith('/profile') ? 'Profile' : (this.store.activeConversation()?.title ?? 'New chat'),
  );

  /** Export needs a saved conversation with at least one message, and not while a reply is still arriving. */
  /** The model picker belongs to chats, not to the profile page. */
  protected readonly onChat = computed(() => !this.url().startsWith('/profile'));

  protected readonly canExport = computed(
    () =>
      !this.url().startsWith('/profile') &&
      this.store.activeConversationId() !== null &&
      this.store.messages().some((m) => m.messageId > 0) &&
      !this.store.sending(),
  );
  protected readonly exporting = signal(false);

  constructor() {
    this.store.loadConversations();
  }

  /** Downloads the whole conversation (all messages, not just the loaded page) as a .txt file from the API. */
  protected exportChat(conversationId: number): void {
    if (this.exporting()) {
      return;
    }
    this.exporting.set(true);
    this.chatApi
      .exportConversation(conversationId)
      .pipe(finalize(() => this.exporting.set(false)))
      .subscribe({
        next: (response) => {
          const title = this.store.conversations().find((c) => c.conversationId === conversationId)?.title ?? 'Chat';
          const fallback = `AIChat - ${safeFileName(title)}.txt`;
          saveBlob(response.body!, fileNameFromDisposition(response.headers.get('Content-Disposition'), fallback));
          this.notifications.success('Chat exported as TXT.');
        },
        error: (err: unknown) => this.notifications.error(`Couldn't export the chat: ${errorMessage(err)}`),
      });
  }

  protected newChat(): void {
    this.store.startNewChat();
    this.closeDrawer();
    this.router.navigate(['/chat']);
  }

  protected closeDrawer(): void {
    this.drawerOpen.set(false);
  }

  protected rename({ conversationId, title }: RenameRequest): void {
    this.store.renameConversation(conversationId, title).subscribe({
      next: () => this.notifications.success('Conversation renamed.'),
      error: (err: unknown) => this.notifications.error(`Couldn't rename: ${errorMessage(err)}`),
    });
  }

  protected pin({ conversation, pinned }: PinRequest): void {
    this.store.setPinned(conversation, pinned).subscribe({
      next: () => this.notifications.success(pinned ? 'Chat pinned.' : 'Chat unpinned.'),
      error: (err: unknown) => this.notifications.error(`Couldn't ${pinned ? 'pin' : 'unpin'} the chat: ${errorMessage(err)}`),
    });
  }

  protected archive({ conversation, archived }: ArchiveRequest): void {
    this.store.setArchived(conversation, archived).subscribe({
      next: () => this.notifications.success(archived ? 'Chat archived. Find it under "Archived chats".' : 'Chat restored.'),
      error: (err: unknown) => this.notifications.error(`Couldn't ${archived ? 'archive' : 'restore'} the chat: ${errorMessage(err)}`),
    });
  }

  protected async remove(conversation: ConversationSummary): Promise<void> {
    const confirmed = await this.confirmDialog.confirm({
      title: 'Delete conversation?',
      message: `"${conversation.title}" and all of its messages will be permanently deleted. This can't be undone.`,
      confirmText: 'Delete',
      danger: true,
    });
    if (!confirmed) {
      return;
    }

    const wasOpen = this.store.activeConversationId() === conversation.conversationId;
    this.store.deleteConversation(conversation.conversationId).subscribe({
      next: () => {
        this.notifications.success('Conversation deleted.');
        if (wasOpen) {
          this.router.navigate(['/chat'], { replaceUrl: true });
        }
      },
      error: (err: unknown) => this.notifications.error(`Couldn't delete: ${errorMessage(err)}`),
    });
  }

  protected logout(): void {
    this.store.reset();
    this.auth.logout();
  }
}
