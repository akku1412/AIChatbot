import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, finalize, map, of, switchMap, tap } from 'rxjs';

import { errorMessage } from '../http/api-error';
import { ChatMessage, ConversationSummary } from '../models/chat.models';
import { ChatApiService } from './chat-api.service';

const PAGE_SIZE = 20;

/**
 * Signal-based state for the chat dashboard: the sidebar's conversation list and the open thread.
 * Components read signals and call methods; only this class talks to ChatApiService.
 */
@Injectable({ providedIn: 'root' })
export class ChatStore {
  private readonly api = inject(ChatApiService);

  // ----- Conversation list (sidebar) -----
  private readonly conversationList = signal<ConversationSummary[]>([]);
  private loadedPage = 0;
  readonly conversations = this.conversationList.asReadonly();
  readonly listLoading = signal(false);
  readonly listError = signal<string | null>(null);
  readonly hasMoreConversations = signal(false);

  // ----- Open conversation -----
  readonly activeConversationId = signal<number | null>(null);
  readonly messages = signal<ChatMessage[]>([]);
  readonly messagesLoading = signal(false);
  readonly messagesError = signal<string | null>(null);
  readonly hasOlderMessages = signal(false);
  readonly sending = signal(false);

  readonly activeConversation = computed(
    () => this.conversationList().find((c) => c.conversationId === this.activeConversationId()) ?? null,
  );

  private nextTempId = -1;

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
          this.conversationList.update((list) => mergeById(list, page.items));
          this.hasMoreConversations.set(page.hasMore);
        },
        error: (err: unknown) => this.listError.set(errorMessage(err)),
      });
  }

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
   * Sends a message, creating the conversation first when this is a new chat.
   * Emits the conversation id so the caller can update the URL. The user's message is shown
   * immediately; on failure it is removed again and the error propagates to the caller.
   */
  send(content: string): Observable<number> {
    const optimistic: ChatMessage = {
      messageId: this.nextTempId--,
      role: 'user',
      content,
      createdAt: new Date().toISOString(),
      tokenUsage: null,
    };
    this.messages.update((list) => [...list, optimistic]);
    this.messagesError.set(null);
    this.sending.set(true);

    const existingId = this.activeConversationId();
    let createdId: number | null = null;
    const conversationId$ = existingId
      ? of(existingId)
      : this.api.createConversation().pipe(
          tap((conversation) => {
            createdId = conversation.conversationId;
            this.conversationList.update((list) => [conversation, ...list]);
            this.activeConversationId.set(conversation.conversationId);
          }),
          map((conversation) => conversation.conversationId),
        );

    return conversationId$.pipe(
      switchMap((conversationId) =>
        this.api.sendMessage(conversationId, content).pipe(
          tap((response) => {
            if (this.activeConversationId() === conversationId) {
              this.messages.update((list) => [
                ...list.filter((m) => m.messageId !== optimistic.messageId),
                response.userMessage,
                response.assistantMessage,
              ]);
            }
            // Move the conversation to the top with its (possibly generated) title.
            this.conversationList.update((list) => [
              response.conversation,
              ...list.filter((c) => c.conversationId !== conversationId),
            ]);
          }),
          map(() => conversationId),
        ),
      ),
      tap({
        error: () => {
          this.messages.update((list) => list.filter((m) => m.messageId !== optimistic.messageId));
          if (createdId !== null) {
            this.discardEmptyConversation(createdId);
          }
        },
      }),
      finalize(() => this.sending.set(false)),
    );
  }

  /**
   * The first message of a new chat failed (e.g. the AI is unavailable), so the conversation created
   * for it has no messages: remove it instead of leaving an empty "New chat" in the sidebar.
   */
  private discardEmptyConversation(conversationId: number): void {
    this.conversationList.update((list) => list.filter((c) => c.conversationId !== conversationId));
    if (this.activeConversationId() === conversationId) {
      this.activeConversationId.set(null);
    }
    this.api.deleteConversation(conversationId).subscribe({ error: () => undefined });
  }

  /** Called on logout so the next user never sees the previous user's data. */
  reset(): void {
    this.conversationList.set([]);
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
