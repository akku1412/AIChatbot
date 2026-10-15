import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';

import { AuthService } from '../../core/auth/auth.service';
import { errorMessage } from '../../core/http/api-error';
import { ConversationSummary } from '../../core/models/chat.models';
import { ChatStore } from '../../core/services/chat.store';
import { AppHeader } from '../../shared/components/app-header/app-header';
import { ConfirmDialogService } from '../../shared/components/confirm-dialog/confirm-dialog.service';
import { RenameRequest, Sidebar } from '../../shared/components/sidebar/sidebar';
import { NotificationService } from '../../shared/components/toast/notification.service';

/** Signed-in shell: sidebar (a drawer on small screens) + header + the routed page (chat or profile). */
@Component({
  selector: 'app-chat-layout',
  imports: [RouterOutlet, Sidebar, AppHeader],
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
          (newChat)="newChat()"
          (selected)="closeDrawer()"
          (closed)="closeDrawer()"
          (loadMore)="store.loadNextConversationPage()"
          (retry)="store.loadConversations()"
          (rename)="rename($event)"
          (remove)="remove($event)"
          (logout)="logout()"
        />
      </aside>

      @if (drawerOpen()) {
        <div class="backdrop" (click)="closeDrawer()" aria-hidden="true"></div>
      }

      <section class="main">
        <app-header [title]="title()" (menu)="drawerOpen.set(true)" (newChat)="newChat()" />
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

  constructor() {
    this.store.loadConversations();
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
