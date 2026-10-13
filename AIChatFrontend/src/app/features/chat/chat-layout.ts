import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';

import { AuthService } from '../../core/auth/auth.service';
import { ChatStore } from '../../core/services/chat.store';
import { AppHeader } from '../../shared/components/app-header/app-header';
import { Sidebar } from '../../shared/components/sidebar/sidebar';

/** Dashboard shell: sidebar (a drawer on small screens) + header + the active thread (router outlet). */
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

  protected readonly drawerOpen = signal(false);
  protected readonly title = computed(() => this.store.activeConversation()?.title ?? 'New chat');

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

  protected logout(): void {
    this.store.reset();
    this.auth.logout();
  }
}
