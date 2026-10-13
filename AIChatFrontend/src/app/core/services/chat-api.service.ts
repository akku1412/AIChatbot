import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  ConversationSummary,
  MessagePage,
  PagedResult,
  SendMessageResponse,
} from '../models/chat.models';

/** Typed HTTP calls for conversations and messages. Ownership is enforced by the API from the JWT. */
@Injectable({ providedIn: 'root' })
export class ChatApiService {
  private readonly http = inject(HttpClient);
  private readonly conversationsUrl = `${environment.apiBaseUrl}/conversations`;
  private readonly chatUrl = `${environment.apiBaseUrl}/chat`;

  listConversations(page: number, pageSize: number): Observable<PagedResult<ConversationSummary>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<PagedResult<ConversationSummary>>(this.conversationsUrl, { params });
  }

  getConversation(conversationId: number): Observable<ConversationSummary> {
    return this.http.get<ConversationSummary>(`${this.conversationsUrl}/${conversationId}`);
  }

  createConversation(title?: string): Observable<ConversationSummary> {
    return this.http.post<ConversationSummary>(this.conversationsUrl, { title: title ?? null });
  }

  renameConversation(conversationId: number, title: string): Observable<ConversationSummary> {
    return this.http.put<ConversationSummary>(`${this.conversationsUrl}/${conversationId}`, { title });
  }

  deleteConversation(conversationId: number): Observable<void> {
    return this.http.delete<void>(`${this.conversationsUrl}/${conversationId}`);
  }

  /** Newest page of messages; pass beforeMessageId to load older ones. Items are oldest-first. */
  getMessages(conversationId: number, beforeMessageId?: number, limit = 50): Observable<MessagePage> {
    let params = new HttpParams().set('limit', limit);
    if (beforeMessageId) {
      params = params.set('before', beforeMessageId);
    }
    return this.http.get<MessagePage>(`${this.chatUrl}/${conversationId}/messages`, { params });
  }

  sendMessage(conversationId: number, content: string): Observable<SendMessageResponse> {
    return this.http.post<SendMessageResponse>(`${this.chatUrl}/${conversationId}/messages`, { content });
  }
}
