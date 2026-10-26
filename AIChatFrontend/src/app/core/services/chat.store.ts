import { Injectable, computed, inject, signal } from '@angular/core';
import { EMPTY, Observable, Subscription, finalize, map, of, switchMap, tap } from 'rxjs';

import { errorMessage } from '../http/api-error';
import {
  AttachmentInfo,
  ChatMessage,
  ChatStreamEvent,
  ConversationPatch,
  ConversationSearchResult,
  ConversationSummary,
  FeedbackRating,
  StreamTurnResult,
} from '../models/chat.models';
import { ChatApiService } from './chat-api.service';
import { ModelService } from './model.service';
import { ProjectService } from './project.service';

const PAGE_SIZE = 20;
/** Streamed text is applied to the screen at most this often (smooth, and Markdown isn't re-rendered per token). */
export const STREAM_RENDER_INTERVAL_MS = 50;

/** What a streaming turn shows while it runs, and how to undo it. */
interface TurnView {
  /** Messages above the question (unchanged by the turn). */
  before: ChatMessage[];
  /** The question shown while streaming: a temporary one (send/edit) or the existing one (regenerate). */
  question: ChatMessage;
  /** The thread as it was before the turn started, restored if the turn fails. */
  snapshot: ChatMessage[];
  /** Regenerate: if stopped before any text, the previous reply is still the visible one. */
  keepSnapshotIfEmpty: boolean;
}

/**
 * Signal-based state for the chat dashboard: the sidebar's conversation lists and the open thread.
 * Components read signals and call methods; only this class talks to ChatApiService.
 */
@Injectable({ providedIn: 'root' })
export class ChatStore {
  private readonly api = inject(ChatApiService);
  private readonly models = inject(ModelService);
  private readonly projects = inject(ProjectService);

  // ----- Conversation lists (sidebar) -----
  private readonly conversationList = signal<ConversationSummary[]>([]);
  private loadedPage = 0;
  readonly conversations = this.conversationList.asReadonly();
  readonly listLoading = signal(false);
  readonly listError = signal<string | null>(null);
  readonly hasMoreConversations = signal(false);

  private readonly archivedList = signal<ConversationSummary[] | null>(null);
  /** Archived chats, or null until they are loaded. */
  readonly archivedConversations = this.archivedList.asReadonly();
  readonly archivedLoading = signal(false);

  /** Search results for the sidebar box, or null when not searching. */
  readonly searchResults = signal<ConversationSearchResult[] | null>(null);
  readonly searchLoading = signal(false);
  readonly searchError = signal<string | null>(null);
  private searchSubscription?: Subscription;

  // ----- Open conversation -----
  readonly activeConversationId = signal<number | null>(null);
  readonly messages = signal<ChatMessage[]>([]);
  readonly messagesLoading = signal(false);
  readonly messagesError = signal<string | null>(null);
  readonly hasOlderMessages = signal(false);
  readonly sending = signal(false);
  /** True while the message being sent carries files (the reply takes longer: documents are read first). */
  readonly sendingWithFiles = signal(false);
  /** True while the message being sent searches the web first. */
  readonly sendingWithWebSearch = signal(false);
  /** The temporary id of the reply that is streaming in right now. */
  readonly streamingMessageId = signal<number | null>(null);
  /** Stop was pressed; waiting for the server to finish saving the partial reply. */
  readonly stopping = signal(false);
  /** True until the first piece of the streaming reply arrives (shows the typing indicator). */
  readonly waitingForFirstToken = computed(() => {
    const id = this.streamingMessageId();
    return id !== null && !this.messages().find((m) => m.messageId === id)?.content;
  });

  /** A chat opened directly (e.g. from search) that isn't in the loaded sidebar pages. */
  private readonly openedConversation = signal<ConversationSummary | null>(null);

  readonly activeConversation = computed(() => {
    const id = this.activeConversationId();
    return (
      [...this.conversationList(), ...(this.archivedList() ?? []), ...Object.values(this.projectLists()).flat()].find(
        (c) => c.conversationId === id,
      ) ?? (this.openedConversation()?.conversationId === id ? this.openedConversation() : null)
    );
  });

  // ----- Projects -----
  /** Loaded chat lists per project id (a project's folder in the sidebar). */
  private readonly projectLists = signal<Record<number, ConversationSummary[]>>({});
  readonly projectConversations = this.projectLists.asReadonly();
  /** The project the next new chat is created in (null = the main list). */
  private readonly newChatProjectId = signal<number | null>(null);
  readonly newChatProject = this.newChatProjectId.asReadonly();

  /** Loads a project's chats (not archived, pinned first, then most recent). */
  loadProjectConversations(projectId: number): void {
    this.api.listConversations(1, 100, false, projectId).subscribe({
      next: (page) => this.projectLists.update((lists) => ({ ...lists, [projectId]: sortConversations(page.items) })),
      error: (err: unknown) => this.listError.set(errorMessage(err)),
    });
  }

  /** The next new chat goes into this project (or the main list with null). */
  newChatIn(projectId: number | null): void {
    this.newChatProjectId.set(projectId);
    this.startNewChat();
  }

  /** Rates an AI reply (null = take the rating back); the thread shows it once the server has saved it. */
  rateMessage(messageId: number, rating: FeedbackRating | null, comment?: string): Observable<void> {
    const request$ = rating === null ? this.api.removeRating(messageId) : this.api.rateMessage(messageId, rating, comment).pipe(map(() => undefined));
    return request$.pipe(
      tap(() => this.messages.update((list) => list.map((m) => (m.messageId === messageId ? { ...m, feedback: rating } : m)))),
    );
  }

  /** Moves a chat into a project, or back to the main list with null. */
  moveToProject(conversation: ConversationSummary, projectId: number | null): Observable<ConversationSummary> {
    const from = conversation.projectId ?? null;
    return this.api.moveConversation(conversation.conversationId, projectId).pipe(
      tap((updated) => {
        this.upsert(updated);
        if (from !== null) {
          this.projects.adjustConversationCount(from, -1);
        }
        if (projectId !== null) {
          this.projects.adjustConversationCount(projectId, +1);
        }
      }),
    );
  }

  /** A deleted project's chats moved back to the main list on the server: show that here too. */
  projectDeleted(projectId: number): void {
    this.projectLists.update(({ [projectId]: _removed, ...rest }) => rest);
    this.openedConversation.update((c) => (c?.projectId === projectId ? { ...c, projectId: null } : c));
    if (this.newChatProjectId() === projectId) {
      this.newChatProjectId.set(null);
    }
    this.loadConversations();
  }

  // ----- Model picker -----
  /** The model picked for the next new chat (sent when the conversation is created). */
  private readonly newChatModelId = signal<string | null>(null);

  /** The model the open chat (or the new chat) will answer with; null until the model list is loaded. */
  readonly activeModelId = computed(() =>
    this.models.effectiveId(this.activeConversationId() ? this.activeConversation()?.modelId : this.newChatModelId()),
  );

  /**
   * Picks the model for the open chat (saved on the server, applies from the next reply) or for the new chat
   * (saved when it is created). Emits once done.
   */
  selectModel(modelId: string): Observable<void> {
    const conversationId = this.activeConversationId();
    if (!conversationId) {
      this.newChatModelId.set(modelId);
      return of(undefined);
    }
    return this.patch(conversationId, { modelId }).pipe(map(() => undefined));
  }

  private activeStream: { abort: (reload?: boolean) => void } | null = null;
  private nextTempId = -1;

  // =============== Sidebar ===============

  loadConversations(): void {
    this.loadedPage = 0;
    this.conversationList.set([]);
    this.loadNextConversationPage();
  }

  loadNextConversationPage(): void {
    if (this.listLoading()) {
      return;
    }
    this.listLoading.set(true);
    this.listError.set(null);

    this.api
      .listConversations(this.loadedPage + 1, PAGE_SIZE)
      .pipe(finalize(() => this.listLoading.set(false)))
      .subscribe({
        next: (page) => {
          this.loadedPage = page.page;
          this.conversationList.update((list) => sortConversations(mergeById(list, page.items)));
          this.hasMoreConversations.set(page.hasMore);
        },
        error: (err: unknown) => this.listError.set(errorMessage(err)),
      });
  }

  loadArchived(): void {
    if (this.archivedLoading()) {
      return;
    }
    this.archivedLoading.set(true);
    this.api
      .listConversations(1, 50, true)
      .pipe(finalize(() => this.archivedLoading.set(false)))
      .subscribe({
        next: (page) => this.archivedList.set(page.items),
        error: (err: unknown) => this.listError.set(errorMessage(err)),
      });
  }

  /** Searches titles and message text; queries shorter than 2 characters clear the results. */
  search(query: string): void {
    this.searchSubscription?.unsubscribe();
    const term = query.trim();
    if (term.length < 2) {
      this.searchResults.set(null);
      this.searchLoading.set(false);
      this.searchError.set(null);
      return;
    }
    this.searchLoading.set(true);
    this.searchError.set(null);
    this.searchSubscription = this.api
      .searchConversations(term)
      .pipe(finalize(() => this.searchLoading.set(false)))
      .subscribe({
        next: (results) => this.searchResults.set(results),
        error: (err: unknown) => this.searchError.set(errorMessage(err)),
      });
  }

  setPinned(conversation: ConversationSummary, isPinned: boolean): Observable<ConversationSummary> {
    return this.patch(conversation.conversationId, { isPinned });
  }

  setArchived(conversation: ConversationSummary, archived: boolean): Observable<ConversationSummary> {
    return this.patch(conversation.conversationId, { archived });
  }

  renameConversation(conversationId: number, title: string): Observable<ConversationSummary> {
    return this.api.renameConversation(conversationId, title).pipe(tap((updated) => this.upsert(updated)));
  }

  /** Deletes on the server first; the sidebar only changes once that succeeded. */
  deleteConversation(conversationId: number): Observable<void> {
    return this.api.deleteConversation(conversationId).pipe(
      tap(() => {
        const projectId = this.projectIdOf(conversationId);
        if (projectId !== null) {
          this.projects.adjustConversationCount(projectId, -1);
        }
        this.removeFromLists(conversationId);
        if (this.activeConversationId() === conversationId) {
          this.startNewChat();
        }
      }),
    );
  }

  private patch(conversationId: number, patch: ConversationPatch): Observable<ConversationSummary> {
    return this.api.patchConversation(conversationId, patch).pipe(tap((updated) => this.upsert(updated)));
  }

  /** Puts a changed conversation in the right list (main or archived), in the right place. */
  private upsert(conversation: ConversationSummary): void {
    const id = conversation.conversationId;
    if (this.openedConversation()?.conversationId === id) {
      this.openedConversation.set(conversation);
    }
    // Each chat is in exactly one list: archived (project or not), its project's folder, or the main list.
    const without = (list: ConversationSummary[]) => list.filter((c) => c.conversationId !== id);
    this.projectLists.update((lists) =>
      Object.fromEntries(Object.entries(lists).map(([projectId, list]) => [projectId, without(list)])),
    );
    if (conversation.archivedAt) {
      this.conversationList.update(without);
      this.archivedList.update((list) => (list ? [conversation, ...without(list)] : list));
    } else if (conversation.projectId != null) {
      const projectId = conversation.projectId;
      this.conversationList.update(without);
      this.archivedList.update((list) => (list ? without(list) : null));
      this.projectLists.update((lists) => ({ ...lists, [projectId]: sortConversations([conversation, ...(lists[projectId] ?? [])]) }));
    } else {
      this.archivedList.update((list) => (list ? without(list) : null));
      this.conversationList.update((list) => sortConversations([conversation, ...without(list)]));
    }
    this.searchResults.update((results) =>
      results?.map((r) => (r.conversationId === id ? { ...r, title: conversation.title, isPinned: conversation.isPinned, archivedAt: conversation.archivedAt } : r)) ??
      null,
    );
  }

  private projectIdOf(conversationId: number): number | null {
    for (const [projectId, list] of Object.entries(this.projectLists())) {
      if (list.some((c) => c.conversationId === conversationId)) {
        return Number(projectId);
      }
    }
    const opened = this.openedConversation();
    return opened?.conversationId === conversationId ? (opened.projectId ?? null) : null;
  }

  private removeFromLists(conversationId: number): void {
    this.projectLists.update((lists) =>
      Object.fromEntries(Object.entries(lists).map(([projectId, list]) => [projectId, list.filter((c) => c.conversationId !== conversationId)])),
    );
    this.conversationList.update((list) => list.filter((c) => c.conversationId !== conversationId));
    this.archivedList.update((list) => list?.filter((c) => c.conversationId !== conversationId) ?? null);
    this.searchResults.update((results) => results?.filter((r) => r.conversationId !== conversationId) ?? null);
  }

  // =============== Thread ===============

  /** Clears the thread for a brand-new chat (the conversation is created on the first message). */
  startNewChat(): void {
    this.activeConversationId.set(null);
    this.messages.set([]);
    this.messagesError.set(null);
    this.hasOlderMessages.set(false);
  }

  openConversation(conversationId: number): void {
    // Already showing it (e.g. we just created it while sending the first message): keep local state.
    if (this.activeConversationId() === conversationId && (this.messages().length > 0 || this.sending())) {
      return;
    }

    this.activeConversationId.set(conversationId);
    this.messages.set([]);
    this.messagesError.set(null);
    this.messagesLoading.set(true);

    // Not in the loaded sidebar pages: fetch its details (title, model) for the header.
    if (!this.activeConversation()) {
      this.api.getConversation(conversationId).subscribe({
        next: (conversation) => this.openedConversation.set(conversation),
        error: () => undefined, // the messages request reports problems (e.g. 404)
      });
    }

    this.api
      .getMessages(conversationId)
      .pipe(finalize(() => this.messagesLoading.set(false)))
      .subscribe({
        next: (page) => {
          if (this.activeConversationId() === conversationId) {
            this.messages.set(page.items);
            this.hasOlderMessages.set(page.hasMore);
          }
        },
        error: (err: unknown) => this.messagesError.set(errorMessage(err)),
      });
  }

  loadOlderMessages(): void {
    const conversationId = this.activeConversationId();
    const oldest = this.messages()[0];
    if (!conversationId || !oldest || this.messagesLoading()) {
      return;
    }

    this.messagesLoading.set(true);
    this.api
      .getMessages(conversationId, oldest.messageId)
      .pipe(finalize(() => this.messagesLoading.set(false)))
      .subscribe({
        next: (page) => {
          this.messages.update((list) => [...page.items, ...list]);
          this.hasOlderMessages.set(page.hasMore);
        },
        error: (err: unknown) => this.messagesError.set(errorMessage(err)),
      });
  }

  /**
   * Sends a message, creating the conversation first when this is a new chat, and streams the reply into the
   * thread. Emits the conversation id once the reply is saved (also when stopped). The user's message shows
   * immediately; on failure it is removed again and the error propagates to the caller.
   */
  send(content: string, attachments: AttachmentInfo[] = [], webSearch = false): Observable<number> {
    const snapshot = this.messages();
    const view: TurnView = {
      before: snapshot,
      question: this.tempMessage('user', content, attachments),
      snapshot,
      keepSnapshotIfEmpty: false,
    };

    const existingId = this.activeConversationId();
    let createdId: number | null = null;
    const conversationId$ = existingId
      ? of(existingId)
      : this.api.createConversation(undefined, this.newChatModelId() ?? undefined, this.newChatProjectId() ?? undefined).pipe(
          tap((conversation) => {
            createdId = conversation.conversationId;
            this.upsert(conversation); // into the main list or its project's folder
            if (conversation.projectId != null) {
              this.projects.adjustConversationCount(conversation.projectId, +1);
            }
            this.activeConversationId.set(conversation.conversationId);
          }),
          map((conversation) => conversation.conversationId),
        );

    return this.runTurn(
      view,
      attachments.length > 0,
      conversationId$.pipe(map((id) => ({ id, events: this.api.streamMessage(id, content, attachments.map((a) => a.fileId), webSearch) }))),
      webSearch,
    ).pipe(
      tap({
        error: () => {
          if (createdId !== null) {
            this.discardEmptyConversation(createdId);
          }
        },
      }),
    );
  }

  /**
   * A new version of this reply (or a first reply, when given a question without one). The old reply is hidden while
   * the new one streams; it stays available as an earlier version ("‹ 1/2"), and comes back if this fails.
   */
  regenerate(messageId: number): Observable<number> {
    const conversationId = this.activeConversationId();
    const list = this.messages();
    const index = list.findIndex((m) => m.messageId === messageId);
    if (!conversationId || this.sending() || index < 0) {
      return EMPTY;
    }
    const target = list[index];
    const questionIndex = target.role === 'user' ? index : index - 1;
    const question = list[questionIndex];
    if (!question || question.role !== 'user') {
      return EMPTY;
    }

    const view: TurnView = { before: list.slice(0, questionIndex), question, snapshot: list, keepSnapshotIfEmpty: true };
    return this.runTurn(view, false, of({ id: conversationId, events: this.api.regenerateMessage(messageId) }));
  }

  /** Edit and resend: replaces the question and everything after it with a new branch (the old one is kept). */
  editMessage(messageId: number, content: string): Observable<number> {
    const conversationId = this.activeConversationId();
    const list = this.messages();
    const index = list.findIndex((m) => m.messageId === messageId);
    if (!conversationId || this.sending() || index < 0 || list[index].role !== 'user') {
      return EMPTY;
    }
    const original = list[index];
    const edited: ChatMessage = {
      ...this.tempMessage('user', content, original.attachments),
      parentMessageId: original.parentMessageId,
      versionNumber: original.versionCount + 1,
      versionCount: original.versionCount + 1,
    };
    const view: TurnView = { before: list.slice(0, index), question: edited, snapshot: list, keepSnapshotIfEmpty: false };
    return this.runTurn(view, original.attachments.length > 0, of({ id: conversationId, events: this.api.editMessage(messageId, content) }));
  }

  /** The "‹ ›" arrows: shows another version of a message and the thread that follows it. */
  switchVersion(messageId: number, versionNumber: number): Observable<void> {
    const conversationId = this.activeConversationId();
    if (!conversationId || this.sending()) {
      return EMPTY;
    }
    return this.api.switchVersion(messageId, versionNumber).pipe(
      tap((page) => {
        if (this.activeConversationId() === conversationId) {
          this.messages.set(page.items);
          this.hasOlderMessages.set(page.hasMore);
        }
      }),
      map(() => undefined),
    );
  }

  /**
   * Stop generating. The server cancels the AI call, saves the partial reply (marked as stopped) and ends the stream
   * with a "done" event carrying the saved messages. If that request fails (or nothing arrives, e.g. the reply runs on
   * another server instance), the fetch is aborted — which also stops and saves it on the server — and the thread reloads.
   */
  stopGeneration(): void {
    const conversationId = this.activeConversationId();
    if (!conversationId || !this.sending() || this.stopping()) {
      return;
    }
    this.stopping.set(true);
    const abort = () => {
      if (this.sending() && this.stopping()) {
        this.activeStream?.abort();
      }
    };
    this.api.stopGeneration(conversationId).subscribe({ error: abort });
    setTimeout(abort, 8000);
  }

  /** Streams one turn into the thread and swaps in the saved messages from the "done" event. */
  private runTurn(
    view: TurnView,
    withFiles: boolean,
    start$: Observable<{ id: number; events: Observable<ChatStreamEvent> }>,
    withWebSearch = false,
  ): Observable<number> {
    const reply = this.tempMessage('assistant', '');
    this.messages.set([...view.before, view.question, reply]);
    this.messagesError.set(null);
    this.sending.set(true);
    this.stopping.set(false);
    this.sendingWithFiles.set(withFiles);
    this.sendingWithWebSearch.set(withWebSearch);
    this.streamingMessageId.set(reply.messageId);

    return start$.pipe(
      switchMap(({ id, events }) => this.consumeStream(id, events, view, reply.messageId)),
      tap({ error: () => this.messages.set(view.snapshot) }),
      finalize(() => this.endStreaming()),
    );
  }

  private consumeStream(conversationId: number, events: Observable<ChatStreamEvent>, view: TurnView, replyId: number): Observable<number> {
    return new Observable<number>((subscriber) => {
      // Deltas are buffered and applied every STREAM_RENDER_INTERVAL_MS.
      let pending = '';
      let timer: ReturnType<typeof setTimeout> | null = null;
      const flush = () => {
        if (timer) {
          clearTimeout(timer);
          timer = null;
        }
        if (pending) {
          const text = pending;
          pending = '';
          this.messages.update((list) => list.map((m) => (m.messageId === replyId ? { ...m, content: m.content + text } : m)));
        }
      };

      const subscription = events.subscribe({
        next: (event) => {
          if (event.type === 'delta') {
            pending += event.text;
            timer ??= setTimeout(flush, STREAM_RENDER_INTERVAL_MS);
            return;
          }
          flush();
          this.applyResult(conversationId, event.result, view);
          subscriber.next(conversationId);
        },
        error: (err: unknown) => {
          flush();
          subscriber.error(err);
        },
        complete: () => {
          flush();
          subscriber.complete();
        },
      });

      this.activeStream = {
        // Aborting the fetch stops generation on the server too; reload to show what it saved.
        abort: (reload = true) => {
          subscription.unsubscribe();
          flush();
          this.endStreaming();
          if (reload) {
            setTimeout(() => this.reloadMessages(conversationId), 1000);
          }
          subscriber.complete();
        },
      };
      return () => {
        if (timer) {
          clearTimeout(timer);
        }
        subscription.unsubscribe();
      };
    });
  }

  /** Swaps the temporary messages for the saved ones from the "done" event. */
  private applyResult(conversationId: number, result: StreamTurnResult, view: TurnView): void {
    if (this.activeConversationId() === conversationId) {
      if (!result.assistantMessage && view.keepSnapshotIfEmpty) {
        // A regeneration stopped before any text: the previous reply is still the visible version.
        this.messages.set(view.snapshot);
      } else {
        this.messages.set([...view.before, result.userMessage, ...(result.assistantMessage ? [result.assistantMessage] : [])]);
      }
    }
    // Update the sidebar entry (title may have been generated; recent activity moves it up).
    this.upsert(result.conversation);
  }

  private reloadMessages(conversationId: number): void {
    this.api.getMessages(conversationId).subscribe({
      next: (page) => {
        if (this.activeConversationId() === conversationId) {
          this.messages.set(page.items);
          this.hasOlderMessages.set(page.hasMore);
        }
      },
      error: () => undefined,
    });
  }

  private tempMessage(role: ChatMessage['role'], content: string, attachments: AttachmentInfo[] = []): ChatMessage {
    return {
      messageId: this.nextTempId--,
      role,
      content,
      createdAt: new Date().toISOString(),
      tokenUsage: null,
      attachments,
      parentMessageId: null,
      versionNumber: 1,
      versionCount: 1,
      isTruncated: false,
    };
  }

  private endStreaming(): void {
    this.sending.set(false);
    this.stopping.set(false);
    this.streamingMessageId.set(null);
    this.activeStream = null;
  }

  /**
   * The first message of a new chat failed (e.g. the AI is unavailable), so the conversation created
   * for it has no messages: remove it instead of leaving an empty "New chat" in the sidebar.
   */
  private discardEmptyConversation(conversationId: number): void {
    this.removeFromLists(conversationId);
    if (this.activeConversationId() === conversationId) {
      this.activeConversationId.set(null);
    }
    this.api.deleteConversation(conversationId).subscribe({ error: () => undefined });
  }

  /** Called on logout so the next user never sees the previous user's data. */
  reset(): void {
    this.activeStream?.abort(false);
    this.conversationList.set([]);
    this.archivedList.set(null);
    this.projectLists.set({});
    this.newChatProjectId.set(null);
    this.projects.reset();
    this.searchSubscription?.unsubscribe();
    this.searchResults.set(null);
    this.loadedPage = 0;
    this.hasMoreConversations.set(false);
    this.listError.set(null);
    this.startNewChat();
  }
}

function mergeById(existing: ConversationSummary[], incoming: ConversationSummary[]): ConversationSummary[] {
  const seen = new Set(existing.map((c) => c.conversationId));
  return [...existing, ...incoming.filter((c) => !seen.has(c.conversationId))];
}

/** Pinned first, then most recently active — the same order as the API. */
export function sortConversations(list: ConversationSummary[]): ConversationSummary[] {
  return [...list].sort(
    (a, b) => Number(b.isPinned) - Number(a.isPinned) || Date.parse(b.updatedAt) - Date.parse(a.updatedAt) || b.conversationId - a.conversationId,
  );
}
