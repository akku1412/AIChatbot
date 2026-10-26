import { Component, DestroyRef, ElementRef, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

import { User } from '../../../core/auth/auth.models';
import { ConversationSearchResult, ConversationSummary, Project } from '../../../core/models/chat.models';
import { AppLogo } from '../app-logo/app-logo';
import { LoadingSpinner } from '../loading-spinner/loading-spinner';

interface ConversationGroup {
  label: string;
  items: ConversationSummary[];
}

export interface RenameRequest {
  conversationId: number;
  title: string;
}

export interface PinRequest {
  conversation: ConversationSummary;
  pinned: boolean;
}

export interface ArchiveRequest {
  conversation: ConversationSummary;
  archived: boolean;
}

export interface MoveRequest {
  conversation: ConversationSummary;
  projectId: number;
}

/** A piece of text, marked when it matches the search. */
export interface TextPart {
  text: string;
  match: boolean;
}

const TITLE_MAX = 200;
/** Wait this long after the last keystroke before searching. */
export const SEARCH_DEBOUNCE_MS = 300;

/** Splits text into parts, marking every case-insensitive occurrence of the query (no HTML involved). */
export function highlightMatches(text: string, query: string): TextPart[] {
  const needle = query.trim().toLowerCase();
  if (!needle) {
    return [{ text, match: false }];
  }
  const parts: TextPart[] = [];
  const haystack = text.toLowerCase();
  let from = 0;
  for (let at = haystack.indexOf(needle); at >= 0; at = haystack.indexOf(needle, from)) {
    if (at > from) {
      parts.push({ text: text.slice(from, at), match: false });
    }
    parts.push({ text: text.slice(at, at + needle.length), match: true });
    from = at + needle.length;
  }
  if (from < text.length) {
    parts.push({ text: text.slice(from), match: false });
  }
  return parts;
}

@Component({
  selector: 'app-sidebar',
  imports: [RouterLink, RouterLinkActive, AppLogo, LoadingSpinner],
  host: { '(document:click)': 'closeMenu()', '(document:keydown.escape)': 'closeMenu()' },
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

      <div class="search" role="search">
        <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><circle cx="11" cy="11" r="6.5" fill="none" stroke="currentColor" stroke-width="2"/><path d="M16 16l4.5 4.5" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>
        <input
          type="search"
          placeholder="Search chats"
          aria-label="Search chats by title or message"
          maxlength="100"
          [value]="query()"
          (input)="onSearchInput($any($event.target).value)"
          (keydown.escape)="clearSearch()"
        />
        @if (query()) {
          <button type="button" class="clear" aria-label="Clear search" (click)="clearSearch()">
            <svg viewBox="0 0 24 24" width="14" height="14" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"/></svg>
          </button>
        }
      </div>

      <div class="list" role="list">
        @if (isSearching()) {
          <h2 class="group-label">Search results</h2>
          @if (searchError(); as message) {
            <p class="state error">{{ message }}</p>
          } @else if (searchResults() === null || (searchLoading() && !searchResults()?.length)) {
            <div class="state"><app-loading-spinner [size]="16" label="Searching" /> Searching…</div>
          } @else if (searchResults()!.length === 0) {
            <p class="state">No chats match “{{ query().trim() }}”.</p>
          } @else {
            @for (result of searchResults(); track result.conversationId) {
              <a
                role="listitem"
                class="result"
                [class.active]="result.conversationId === activeId()"
                [routerLink]="['/chat', result.conversationId]"
                (click)="selected.emit()"
              >
                <span class="result-title">
                  @for (part of highlight(result.title); track $index) {
                    @if (part.match) { <mark>{{ part.text }}</mark> } @else { <span>{{ part.text }}</span> }
                  }
                  @if (result.isPinned) {<span class="tag">Pinned</span>}
                  @if (result.archivedAt) {<span class="tag">Archived</span>}
                </span>
                @if (result.snippet; as snippet) {
                  <span class="result-snippet">
                    @for (part of highlight(snippet); track $index) {
                      @if (part.match) { <mark>{{ part.text }}</mark> } @else { <span>{{ part.text }}</span> }
                    }
                  </span>
                }
              </a>
            }
          }
        } @else if (showArchived()) {
          <div class="archived-head">
            <button type="button" class="link back" (click)="showArchived.set(false)">
              <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M15 6l-6 6 6 6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>
              Back to chats
            </button>
          </div>
          <h2 class="group-label">Archived</h2>
          @if (archivedLoading() && archived() === null) {
            <div class="state"><app-loading-spinner [size]="16" label="Loading archived chats" /> Loading…</div>
          } @else if (!archived()?.length) {
            <p class="state">No archived chats.</p>
          } @else {
            @for (conversation of archived(); track conversation.conversationId) {
              <div role="listitem" class="row" [class.active]="conversation.conversationId === activeId()">
                <a class="item" [routerLink]="['/chat', conversation.conversationId]" [title]="conversation.title" (click)="selected.emit()">{{ conversation.title }}</a>
                <button type="button" class="more-button visible" [attr.aria-label]="'Unarchive ' + conversation.title" title="Unarchive"
                        (click)="archive.emit({ conversation, archived: false })">
                  <svg viewBox="0 0 24 24" width="17" height="17" aria-hidden="true"><path d="M4 7h16v13H4zM3 4h18v3H3zM12 17v-6M9 13.5l3-3 3 3" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>
                </button>
              </div>
            }
          }
        } @else {
        <ng-content select="[sidebar-projects]" />
        @for (group of groups(); track group.label) {
          <h2 class="group-label">{{ group.label }}</h2>
          @for (conversation of group.items; track conversation.conversationId) {
            <div role="listitem" class="row" [class.active]="conversation.conversationId === activeId()"
                 [class.menu-open]="openMenuId() === conversation.conversationId">
              @if (editingId() === conversation.conversationId) {
                <input
                  #renameInput
                  class="rename"
                  type="text"
                  [value]="conversation.title"
                  [attr.maxlength]="titleMax"
                  aria-label="Conversation title"
                  (keydown.enter)="commitRename(conversation, $event)"
                  (keydown.escape)="cancelRename($event)"
                  (blur)="commitRename(conversation, $event)"
                />
              } @else {
                <a
                  class="item"
                  [attr.aria-current]="conversation.conversationId === activeId() ? 'page' : null"
                  [routerLink]="['/chat', conversation.conversationId]"
                  [title]="conversation.title"
                  (click)="selected.emit()"
                >{{ conversation.title }}</a>
                <button
                  type="button"
                  class="more-button"
                  [attr.aria-label]="'Options for ' + conversation.title"
                  aria-haspopup="menu"
                  [attr.aria-expanded]="openMenuId() === conversation.conversationId"
                  (click)="toggleMenu(conversation.conversationId, $event)"
                >
                  <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><circle cx="5" cy="12" r="1.6" fill="currentColor"/><circle cx="12" cy="12" r="1.6" fill="currentColor"/><circle cx="19" cy="12" r="1.6" fill="currentColor"/></svg>
                </button>
                @if (openMenuId() === conversation.conversationId) {
                  <div class="menu" role="menu" (click)="$event.stopPropagation()">
                    <button type="button" role="menuitem" (click)="requestPin(conversation)">
                      <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M9 4h6l-1 6 4 4H6l4-4zM12 14v7" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>
                      {{ conversation.isPinned ? 'Unpin' : 'Pin' }}
                    </button>
                    <button type="button" role="menuitem" (click)="startRename(conversation.conversationId)">
                      <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M4 20h4L19 9l-4-4L4 16z" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"/></svg>
                      Rename
                    </button>
                    @if (projects().length) {
                      <p class="menu-label" role="presentation">Move to project</p>
                      @for (project of projects(); track project.projectId) {
                        <button type="button" role="menuitem" class="indent" (click)="requestMove(conversation, project.projectId)">
                          <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"/></svg>
                          <span class="menu-text">{{ project.name }}</span>
                        </button>
                      }
                    }
                    <button type="button" role="menuitem" (click)="requestExport(conversation)">
                      <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M12 4v11M7 10l5 5 5-5M5 20h14" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>
                      Export as TXT
                    </button>
                    <button type="button" role="menuitem" (click)="requestArchive(conversation)">
                      <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M4 7h16v13H4zM3 4h18v3H3zM10 11h4" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>
                      Archive
                    </button>
                    <button type="button" role="menuitem" class="danger" (click)="requestDelete(conversation)">
                      <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>
                      Delete
                    </button>
                  </div>
                }
              }
            </div>
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
        }
      </div>

      @if (!isSearching() && !showArchived()) {
        <button type="button" class="archived-link" (click)="openArchivedList()">
          <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M4 7h16v13H4zM3 4h18v3H3zM10 11h4" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>
          Archived chats
        </button>
      }

      @if (user(); as u) {
        <div class="profile">
          <a class="profile-link" routerLink="/profile" routerLinkActive="active" title="Your profile" (click)="selected.emit()">
            @if (u.profileImageUrl && !imageFailed()) {
              <img class="avatar" [src]="u.profileImageUrl" alt="" referrerpolicy="no-referrer" (error)="imageFailed.set(true)" />
            } @else {
              <span class="avatar initials" aria-hidden="true">{{ initials() }}</span>
            }
            <span class="who">
              <span class="name">{{ u.fullName }}</span>
              <span class="email">{{ u.email }}</span>
            </span>
          </a>
          <button type="button" class="icon-button" [attr.aria-label]="theme() === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'"
                  [title]="theme() === 'dark' ? 'Light theme' : 'Dark theme'" (click)="toggleTheme.emit()">
            @if (theme() === 'dark') {
              <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><circle cx="12" cy="12" r="4" fill="none" stroke="currentColor" stroke-width="2"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>
            } @else {
              <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><path d="M20 14.5A8 8 0 1 1 9.5 4a6.5 6.5 0 0 0 10.5 10.5z" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round"/></svg>
            }
          </button>
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
  /** Results for the current search (null = not searched yet). */
  readonly searchResults = input<ConversationSearchResult[] | null>(null);
  readonly searchLoading = input(false);
  readonly searchError = input<string | null>(null);
  /** Archived chats (null = not loaded yet). */
  readonly archived = input<ConversationSummary[] | null>(null);
  readonly archivedLoading = input(false);
  /** Targets for "Move to project" in each chat's menu. */
  readonly projects = input<Pick<Project, 'projectId' | 'name'>[]>([]);
  /** What is on screen, for the sun/moon button. */
  readonly theme = input<'light' | 'dark'>('light');

  /** Debounced search text ("" when cleared). */
  readonly search = output<string>();
  readonly pin = output<PinRequest>();
  readonly archive = output<ArchiveRequest>();
  readonly openArchived = output<void>();
  readonly newChat = output<void>();
  readonly selected = output<void>();
  readonly loadMore = output<void>();
  readonly retry = output<void>();
  readonly logout = output<void>();
  readonly closed = output<void>();
  readonly rename = output<RenameRequest>();
  readonly remove = output<ConversationSummary>();
  readonly exportChat = output<ConversationSummary>();
  readonly move = output<MoveRequest>();
  readonly toggleTheme = output<void>();

  protected readonly titleMax = TITLE_MAX;
  protected readonly imageFailed = signal(false);
  protected readonly openMenuId = signal<number | null>(null);
  protected readonly editingId = signal<number | null>(null);
  protected readonly query = signal('');
  protected readonly showArchived = signal(false);
  /** Searching starts at 2 characters (the API's minimum). */
  protected readonly isSearching = computed(() => this.query().trim().length >= 2);
  private readonly renameInput = viewChild<ElementRef<HTMLInputElement>>('renameInput');
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.searchTimer && clearTimeout(this.searchTimer));
  }

  protected onSearchInput(value: string): void {
    this.query.set(value);
    if (this.searchTimer) {
      clearTimeout(this.searchTimer);
    }
    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      this.search.emit(value.trim().length >= 2 ? value.trim() : '');
    }, SEARCH_DEBOUNCE_MS);
  }

  protected clearSearch(): void {
    if (this.searchTimer) {
      clearTimeout(this.searchTimer);
      this.searchTimer = null;
    }
    this.query.set('');
    this.search.emit('');
  }

  protected highlight(text: string): TextPart[] {
    return highlightMatches(text, this.query());
  }

  protected openArchivedList(): void {
    this.showArchived.set(true);
    this.openArchived.emit();
  }

  protected requestPin(conversation: ConversationSummary): void {
    this.closeMenu();
    this.pin.emit({ conversation, pinned: !conversation.isPinned });
  }

  protected requestArchive(conversation: ConversationSummary): void {
    this.closeMenu();
    this.archive.emit({ conversation, archived: true });
  }

  protected readonly initials = computed(() =>
    (this.user()?.fullName ?? '?')
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]!.toUpperCase())
      .join(''),
  );

  /** Pinned chats first, then sections by last activity (local time), like most chat apps. */
  protected readonly groups = computed<ConversationGroup[]>(() => {
    const startOfToday = new Date();
    startOfToday.setHours(0, 0, 0, 0);
    const today = startOfToday.getTime();
    const yesterday = today - 86_400_000;
    const weekAgo = today - 7 * 86_400_000;

    const buckets: ConversationGroup[] = [
      { label: 'Pinned', items: [] },
      { label: 'Today', items: [] },
      { label: 'Yesterday', items: [] },
      { label: 'Previous 7 days', items: [] },
      { label: 'Older', items: [] },
    ];
    for (const conversation of this.conversations()) {
      if (conversation.isPinned) {
        buckets[0]!.items.push(conversation);
        continue;
      }
      const updated = Date.parse(conversation.updatedAt);
      const index = updated >= today ? 1 : updated >= yesterday ? 2 : updated >= weekAgo ? 3 : 4;
      buckets[index]!.items.push(conversation);
    }
    return buckets.filter((group) => group.items.length > 0);
  });

  protected toggleMenu(conversationId: number, event: MouseEvent): void {
    event.stopPropagation(); // keep the document click handler from closing it straight away
    this.openMenuId.update((open) => (open === conversationId ? null : conversationId));
  }

  protected closeMenu(): void {
    this.openMenuId.set(null);
  }

  protected startRename(conversationId: number): void {
    this.closeMenu();
    this.editingId.set(conversationId);
    setTimeout(() => {
      const input = this.renameInput()?.nativeElement;
      input?.focus();
      input?.select();
    });
  }

  protected commitRename(conversation: ConversationSummary, event: Event): void {
    if (this.editingId() !== conversation.conversationId) {
      return; // already handled (Enter, then blur fires as the input is removed)
    }
    event.preventDefault();
    const title = (event.target as HTMLInputElement).value.trim();
    this.editingId.set(null);
    if (title && title !== conversation.title) {
      this.rename.emit({ conversationId: conversation.conversationId, title: title.slice(0, TITLE_MAX) });
    }
  }

  protected cancelRename(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    this.editingId.set(null);
  }

  protected requestMove(conversation: ConversationSummary, projectId: number): void {
    this.closeMenu();
    this.move.emit({ conversation, projectId });
  }

  protected requestExport(conversation: ConversationSummary): void {
    this.closeMenu();
    this.exportChat.emit(conversation);
  }

  protected requestDelete(conversation: ConversationSummary): void {
    this.closeMenu();
    this.remove.emit(conversation);
  }
}
