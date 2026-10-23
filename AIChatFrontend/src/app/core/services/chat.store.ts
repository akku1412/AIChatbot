import { Injectable, computed, inject, signal } from '@angular/core';
import { EMPTY, Observable, finalize, map, of, switchMap, tap } from 'rxjs';

import { errorMessage } from '../http/api-error';
import { AttachmentInfo, ChatMessage, ChatStreamEvent, ConversationSummary, StreamTurnResult } from '../models/chat.models';
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
  /** True while the message being sent carries files (the reply takes longer: documents are read first). */
  readonly sendingWithFiles = signal(false);
  /** The temporary id of the reply that is streaming in right now. */
  readonly streamingMessageId = signal<number | null>(null);
  /** Stop was pressed; waiting for the server to finish saving the partial reply. */
  readonly stopping = signal(false);
  /** True until the first piece of the streaming reply arrives (shows the typing indicator). */
  readonly waitingForFirstToken = computed(() => {
    const id = this.streamingMessageId();
    return id !== null && !this.messages().find((m) => m.messageId === id)?.content;
  });

  private activeStream: { abort: () => void } | null = null;

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
   * Sends a message, creating the conversation first when this is a new chat, and streams the reply into the
   * thread. Emits the conversation id once the reply is saved (also when stopped). The user's message shows
   * immediately; on failure it is removed again and the error propagates to the caller.
   */
  send(content: string, attachments: AttachmentInfo[] = []): Observable<number> {
    const question = this.tempMessage('user', content, attachments);
    const reply = this.tempMessage('assistant', '');
    this.messages.update((list) => [...list, question, reply]);
    this.beginStreaming(reply.messageId, attachments.length > 0);

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
        this.consumeStream(
          conversationId,
          this.api.streamMessage(conversationId, content, attachments.map((a) => a.fileId)),
          { questionTempId: question.messageId, replyTempId: reply.messageId, replaced: null },
        ),
      ),
      tap({
        error: () => {
          this.messages.update((list) => list.filter((m) => m.messageId !== question.messageId && m.messageId !== reply.messageId));
          if (createdId !== null) {
            this.discardEmptyConversation(createdId);
          }
        },
      }),
      finalize(() => this.endStreaming()),
    );
  }

  /**
   * Streams a new answer to the last question. The old reply disappears while the new one streams and comes
   * back if regeneration fails or is stopped before any text.
   */
  regenerate(): Observable<number> {
    const conversationId = this.activeConversationId();
    if (!conversationId || this.sending()) {
      return EMPTY;
    }
    const last = this.messages().at(-1);
    const replaced = last?.role === 'assistant' ? last : null;
    const reply = this.tempMessage('assistant', '');
    this.messages.update((list) => [...list.filter((m) => m !== replaced), reply]);
    this.beginStreaming(reply.messageId, false);

    return this.consumeStream(conversationId, this.api.regenerate(conversationId), {
      questionTempId: null,
      replyTempId: reply.messageId,
      replaced,
    }).pipe(
      tap({
        error: () =>
          this.messages.update((list) => [...list.filter((m) => m.messageId !== reply.messageId), ...(replaced ? [replaced] : [])]),
      }),
      finalize(() => this.endStreaming()),
    );
  }

  /**
   * Stop generating: the server ends the stream with a "done" (stopped) event and keeps the partial reply.
   * If no such event arrives (e.g. the reply ran on another server instance), the stream is closed from here,
   * which also stops and saves it on the server, and the thread is reloaded.
   */
  stopGeneration(): void {
    const conversationId = this.activeConversationId();
    if (!conversationId || !this.sending()) {
      return;
    }
    this.stopping.set(true);
    this.api.stopGeneration(conversationId).subscribe({ error: () => undefined });
    setTimeout(() => {
      if (this.sending() && this.stopping() && this.activeStream) {
        this.activeStream.abort();
      }
    }, 8000);
  }

  private consumeStream(
    conversationId: number,
    events: Observable<ChatStreamEvent>,
    temp: { questionTempId: number | null; replyTempId: number; replaced: ChatMessage | null },
  ): Observable<number> {
    return new Observable<number>((subscriber) => {
      const subscription = events.subscribe({
        next: (event) => {
          if (event.type === 'delta') {
            this.messages.update((list) =>
              list.map((m) => (m.messageId === temp.replyTempId ? { ...m, content: m.content + event.text } : m)),
            );
            return;
          }
          this.applyResult(conversationId, event.result, temp);
          subscriber.next(conversationId);
        },
        error: (err: unknown) => subscriber.error(err),
        complete: () => subscriber.complete(),
      });

      this.activeStream = {
        // Closing the stream stops generation on the server too; reload to show what it saved.
        abort: () => {
          subscription.unsubscribe();
          this.endStreaming();
          this.reloadMessages(conversationId);
          subscriber.complete();
        },
      };
      return () => subscription.unsubscribe();
    });
  }

  /** Swaps the temporary messages for the saved ones from the "done" event. */
  private applyResult(
    conversationId: number,
    result: StreamTurnResult,
    temp: { questionTempId: number | null; replyTempId: number; replaced: ChatMessage | null },
  ): void {
    if (this.activeConversationId() === conversationId) {
      this.messages.update((list) => {
        const kept = list.filter(
          (m) => m.messageId !== temp.questionTempId && m.messageId !== temp.replyTempId && m.messageId !== result.replacedMessageId,
        );
        const added: ChatMessage[] = [];
        if (temp.questionTempId !== null) {
          added.push(result.userMessage);
        }
        if (result.assistantMessage) {
          added.push(result.assistantMessage);
        } else if (temp.replaced) {
          added.push(temp.replaced); // stopped before any text: the old reply stays
        }
        return [...kept, ...added];
      });
    }
    // Move the conversation to the top with its (possibly generated) title.
    this.conversationList.update((list) => [result.conversation, ...list.filter((c) => c.conversationId !== conversationId)]);
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
    return { messageId: this.nextTempId--, role, content, createdAt: new Date().toISOString(), tokenUsage: null, attachments };
  }

  private beginStreaming(replyTempId: number, withFiles: boolean): void {
    this.messagesError.set(null);
    this.sending.set(true);
    this.stopping.set(false);
    this.sendingWithFiles.set(withFiles);
    this.streamingMessageId.set(replyTempId);
  }

  private endStreaming(): void {
    this.sending.set(false);
    this.stopping.set(false);
    this.streamingMessageId.set(null);
    this.activeStream = null;
  }

  renameConversation(conversationId: number, title: string): Observable<ConversationSummary> {
    return this.api.renameConversation(conversationId, title).pipe(
      tap((updated) =>
        this.conversationList.update((list) =>
          list.map((c) => (c.conversationId === conversationId ? updated : c)),
        ),
      ),
    );
  }

  /** Deletes on the server first; the sidebar only changes once that succeeded. */
  deleteConversation(conversationId: number): Observable<void> {
    return this.api.deleteConversation(conversationId).pipe(
      tap(() => {
        this.conversationList.update((list) => list.filter((c) => c.conversationId !== conversationId));
        if (this.activeConversationId() === conversationId) {
          this.startNewChat();
        }
      }),
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
