import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { readBlobErrors } from '../http/api-error';
import {
  ChatStreamEvent,
  ConversationPatch,
  ConversationSearchResult,
  ConversationSummary,
  ExportFormat,
  MessagePage,
  PagedResult,
  SendMessageResponse,
} from '../models/chat.models';
import { ChatStreamService } from './chat-stream.service';

/** Typed HTTP calls for conversations and messages. Ownership is enforced by the API from the JWT. */
@Injectable({ providedIn: 'root' })
export class ChatApiService {
  private readonly http = inject(HttpClient);
  private readonly streams = inject(ChatStreamService);
  private readonly conversationsUrl = `${environment.apiBaseUrl}/conversations`;
  private readonly chatUrl = `${environment.apiBaseUrl}/chat`;
  private readonly messagesUrl = `${environment.apiBaseUrl}/messages`;

  /**
   * The main list (pinned first; chats in no project), with archived = true all archived chats, or with a
   * projectId that project's chats.
   */
  listConversations(page: number, pageSize: number, archived = false, projectId?: number): Observable<PagedResult<ConversationSummary>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize).set('archived', archived);
    if (projectId !== undefined) {
      params = params.set('projectId', projectId);
    }
    return this.http.get<PagedResult<ConversationSummary>>(this.conversationsUrl, { params });
  }

  /** Chats whose title or messages contain the text (2–100 characters). */
  searchConversations(query: string): Observable<ConversationSearchResult[]> {
    return this.http.get<ConversationSearchResult[]>(`${this.conversationsUrl}/search`, { params: { q: query } });
  }

  getConversation(conversationId: number): Observable<ConversationSummary> {
    return this.http.get<ConversationSummary>(`${this.conversationsUrl}/${conversationId}`);
  }

  /** modelId: an id from GET /api/models (omitted = the default model); projectId: create it inside that project. */
  createConversation(title?: string, modelId?: string, projectId?: number): Observable<ConversationSummary> {
    return this.http.post<ConversationSummary>(this.conversationsUrl, {
      title: title ?? null,
      modelId: modelId ?? null,
      projectId: projectId ?? null,
    });
  }

  /** Moves a chat into a project, or back to the main list with null. */
  moveConversation(conversationId: number, projectId: number | null): Observable<ConversationSummary> {
    return this.http.put<ConversationSummary>(`${this.conversationsUrl}/${conversationId}/project`, { projectId });
  }

  renameConversation(conversationId: number, title: string): Observable<ConversationSummary> {
    return this.http.put<ConversationSummary>(`${this.conversationsUrl}/${conversationId}`, { title });
  }

  /** Pin/unpin, archive/unarchive or rename. */
  patchConversation(conversationId: number, patch: ConversationPatch): Observable<ConversationSummary> {
    return this.http.patch<ConversationSummary>(`${this.conversationsUrl}/${conversationId}`, patch);
  }

  deleteConversation(conversationId: number): Observable<void> {
    return this.http.delete<void>(`${this.conversationsUrl}/${conversationId}`);
  }

  /** The whole conversation as a .txt file (the server names it in Content-Disposition). */
  exportConversation(conversationId: number): Observable<HttpResponse<Blob>> {
    return this.http
      .get(`${this.conversationsUrl}/${conversationId}/export`, { observe: 'response', responseType: 'blob' })
      .pipe(readBlobErrors());
  }

  /** One message (an AI reply) generated on the server as a real TXT, PDF, DOCX or XLSX file. */
  exportMessage(messageId: number, format: ExportFormat): Observable<HttpResponse<Blob>> {
    return this.http
      .get(`${this.messagesUrl}/${messageId}/export`, { params: { format }, observe: 'response', responseType: 'blob' })
      .pipe(readBlobErrors());
  }

  /** Newest page of the visible thread; pass beforeMessageId to load older messages. Items are oldest-first. */
  getMessages(conversationId: number, beforeMessageId?: number, limit = 50): Observable<MessagePage> {
    let params = new HttpParams().set('limit', limit);
    if (beforeMessageId) {
      params = params.set('before', beforeMessageId);
    }
    return this.http.get<MessagePage>(`${this.chatUrl}/${conversationId}/messages`, { params });
  }

  /** Non-streaming fallback: attachmentIds come from POST /api/files/upload; content may be empty with files. */
  sendMessage(conversationId: number, content: string, attachmentIds: string[] = []): Observable<SendMessageResponse> {
    return this.http.post<SendMessageResponse>(`${this.chatUrl}/${conversationId}/messages`, { content, attachmentIds });
  }

  /** Sends a message and streams the reply: "delta" pieces, then "done" with the saved messages. */
  streamMessage(conversationId: number, content: string, attachmentIds: string[] = []): Observable<ChatStreamEvent> {
    return this.streams.stream(`${this.chatUrl}/${conversationId}/messages/stream`, 'POST', { content, attachmentIds });
  }

  /** Streams a new version of this AI reply (or a first reply, for a question without one). */
  regenerateMessage(messageId: number): Observable<ChatStreamEvent> {
    return this.streams.stream(`${this.messagesUrl}/${messageId}/regenerate`, 'POST', {});
  }

  /** Edit and resend: a new version of this user message, then a streamed reply on the new branch. */
  editMessage(messageId: number, content: string): Observable<ChatStreamEvent> {
    return this.streams.stream(`${this.messagesUrl}/${messageId}`, 'PUT', { content });
  }

  /** Shows another version of a message; returns the newest page of the resulting thread. */
  switchVersion(messageId: number, versionNumber: number): Observable<MessagePage> {
    return this.http.post<MessagePage>(`${this.messagesUrl}/${messageId}/switch-version`, { versionNumber });
  }

  /** Asks the server to stop the reply being generated; the stream then ends with a "done" (stopped) event. */
  stopGeneration(conversationId: number): Observable<void> {
    return this.http.post<void>(`${this.chatUrl}/${conversationId}/messages/stop`, {});
  }
}
