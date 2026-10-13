import { Component, computed, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { User } from '../../../core/auth/auth.models';
import { ConversationSummary } from '../../../core/models/chat.models';
import { AppLogo } from '../app-logo/app-logo';
import { LoadingSpinner } from '../loading-spinner/loading-spinner';

interface ConversationGroup {
  label: string;
  items: ConversationSummary[];
}

@Component({
  selector: 'app-sidebar',
  imports: [RouterLink, AppLogo, LoadingSpinner],
  template: `
    <nav class="sidebar" aria-label="Conversations">
      <div class="top">
        <a class="brand" routerLink="/chat" (click)="newChat.emit()">
          <app-logo [size]="28" />
          <span>AIChat</span>
        </a>
        <button type="button" class="icon-button close" aria-label="Close sidebar" (click)="closed.emit()">
          <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>
        </button>
      </div>

      <button type="button" class="new-chat" (click)="newChat.emit()">
        <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><path d="M12 5v14M5 12h14" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>
        New chat
      </button>

      <div class="list" role="list">
        @for (group of groups(); track group.label) {
          <h2 class="group-label">{{ group.label }}</h2>
          @for (conversation of group.items; track conversation.conversationId) {
            <a
              role="listitem"
              class="item"
              [class.active]="conversation.conversationId === activeId()"
              [attr.aria-current]="conversation.conversationId === activeId() ? 'page' : null"
              [routerLink]="['/chat', conversation.conversationId]"
              [title]="conversation.title"
              (click)="selected.emit()"
            >{{ conversation.title }}</a>
          }
        }

        @if (loading()) {
          <div class="state"><app-loading-spinner [size]="16" label="Loading conversations" /> Loading…</div>
        } @else if (error(); as message) {
          <div class="state error">
            <span>Couldn't load conversations.</span>
            <button type="button" class="link" (click)="retry.emit()">Retry</button>
          </div>
        } @else if (conversations().length === 0) {
          <p class="state">No conversations yet. Start one with <strong>New chat</strong>.</p>
        } @else if (hasMore()) {
          <button type="button" class="link more" (click)="loadMore.emit()">Show more</button>
        }
      </div>

      @if (user(); as u) {
        <div class="profile">
          @if (u.profileImageUrl && !imageFailed()) {
            <img class="avatar" [src]="u.profileImageUrl" alt="" referrerpolicy="no-referrer" (error)="imageFailed.set(true)" />
          } @else {
            <span class="avatar initials" aria-hidden="true">{{ initials() }}</span>
          }
          <div class="who">
            <span class="name">{{ u.fullName }}</span>
            <span class="email">{{ u.email }}</span>
          </div>
          <button type="button" class="icon-button" aria-label="Log out" title="Log out" (click)="logout.emit()">
            <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><path d="M15 4h3a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-3M10 17l5-5-5-5M15 12H4" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>
          </button>
        </div>
      }
    </nav>
  `,
  styleUrl: './sidebar.css',
})
export class Sidebar {
  readonly conversations = input.required<ConversationSummary[]>();
  readonly activeId = input<number | null>(null);
  readonly loading = input(false);
  readonly error = input<string | null>(null);
  readonly hasMore = input(false);
  readonly user = input<User | null>(null);

  readonly newChat = output<void>();
  readonly selected = output<void>();
  readonly loadMore = output<void>();
  readonly retry = output<void>();
  readonly logout = output<void>();
  readonly closed = output<void>();

  protected readonly imageFailed = signal(false);

  protected readonly initials = computed(() =>
    (this.user()?.fullName ?? '?')
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]!.toUpperCase())
      .join(''),
  );

  /** Sidebar sections by last activity, like most chat apps. */
  protected readonly groups = computed<ConversationGroup[]>(() => {
    const startOfToday = new Date();
    startOfToday.setHours(0, 0, 0, 0);
    const today = startOfToday.getTime();
    const yesterday = today - 86_400_000;
    const weekAgo = today - 7 * 86_400_000;

    const buckets: ConversationGroup[] = [
      { label: 'Today', items: [] },
      { label: 'Yesterday', items: [] },
      { label: 'Previous 7 days', items: [] },
      { label: 'Older', items: [] },
    ];
    for (const conversation of this.conversations()) {
      const updated = Date.parse(conversation.updatedAt);
      const index = updated >= today ? 0 : updated >= yesterday ? 1 : updated >= weekAgo ? 2 : 3;
      buckets[index]!.items.push(conversation);
    }
    return buckets.filter((group) => group.items.length > 0);
  });
}
