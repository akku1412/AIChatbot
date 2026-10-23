import {
  HttpClient,
  HttpDownloadProgressEvent,
  HttpErrorResponse,
  HttpEventType,
  HttpParams,
  HttpResponse,
} from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { readBlobErrors } from '../http/api-error';
import {
  ChatStreamEvent,
  ConversationSummary,
  ExportFormat,
  MessagePage,
  PagedResult,
  SendMessageResponse,
  StreamTurnResult,
} from '../models/chat.models';
import { SseParser } from './sse-parser';

/** Typed HTTP calls for conversations and messages. Ownership is enforced by the API from the JWT. */
@Injectable({ providedIn: 'root' })
export class ChatApiService {
  private readonly http = inject(HttpClient);
  private readonly conversationsUrl = `${environment.apiBaseUrl}/conversations`;
  private readonly chatUrl = `${environment.apiBaseUrl}/chat`;
  private readonly messagesUrl = `${environment.apiBaseUrl}/messages`;

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

  /** Newest page of messages; pass beforeMessageId to load older ones. Items are oldest-first. */
  getMessages(conversationId: number, beforeMessageId?: number, limit = 50): Observable<MessagePage> {
    let params = new HttpParams().set('limit', limit);
    if (beforeMessageId) {
      params = params.set('before', beforeMessageId);
    }
    return this.http.get<MessagePage>(`${this.chatUrl}/${conversationId}/messages`, { params });
  }

  /** attachmentIds come from POST /api/files/upload; content may be empty when files are attached. */
  sendMessage(conversationId: number, content: string, attachmentIds: string[] = []): Observable<SendMessageResponse> {
    return this.http.post<SendMessageResponse>(`${this.chatUrl}/${conversationId}/messages`, { content, attachmentIds });
  }

  /** Sends a message and streams the reply: "delta" pieces, then "done" with the saved messages. */
  streamMessage(conversationId: number, content: string, attachmentIds: string[] = []): Observable<ChatStreamEvent> {
    return this.stream(`${this.chatUrl}/${conversationId}/messages/stream`, { content, attachmentIds });
  }

  /** Streams a new answer to the conversation's last question, replacing the previous reply. */
  regenerate(conversationId: number): Observable<ChatStreamEvent> {
    return this.stream(`${this.chatUrl}/${conversationId}/messages/regenerate`, {});
  }

  /** Asks the server to stop the reply being generated; the stream then ends with a "done" (stopped) event. */
  stopGeneration(conversationId: number): Observable<void> {
    return this.http.post<void>(`${this.chatUrl}/${conversationId}/messages/stop`, {});
  }

  /**
   * POSTs and reads the Server-Sent Events response as it arrives (XHR progress events expose the partial text),
   * so the auth interceptor (token + refresh on 401) still applies. Unsubscribing aborts the request.
   * An "error" event, or a stream that ends without "done", errors the Observable with an HttpErrorResponse.
   */
  private stream(url: string, body: object): Observable<ChatStreamEvent> {
    return new Observable<ChatStreamEvent>((subscriber) => {
      const parser = new SseParser();
      let consumed = 0;
      let done = false;

      const consume = (text: string): void => {
        for (const sse of parser.push(text.slice(consumed))) {
          const data = JSON.parse(sse.data) as Record<string, unknown>;
          if (sse.event === 'delta') {
            subscriber.next({ type: 'delta', text: String(data['text'] ?? '') });
          } else if (sse.event === 'done') {
            done = true;
            subscriber.next({ type: 'done', result: data as unknown as StreamTurnResult });
          } else if (sse.event === 'error') {
            subscriber.error(new HttpErrorResponse({ status: Number(data['status']) || 500, error: data, url }));
          }
        }
        consumed = text.length;
      };

      const request = this.http
        .post(url, body, { observe: 'events', reportProgress: true, responseType: 'text' })
        .pipe(readBlobErrors())
        .subscribe({
          next: (event) => {
            if (event.type === HttpEventType.DownloadProgress) {
              consume((event as HttpDownloadProgressEvent).partialText ?? '');
            } else if (event.type === HttpEventType.Response) {
              consume(event.body ?? '');
              if (subscriber.closed) {
                return;
              }
              if (done) {
                subscriber.complete();
              } else {
                subscriber.error(new HttpErrorResponse({
                  status: 0,
                  url,
                  error: { title: 'Reply interrupted', detail: 'The connection was lost before the reply finished. Please try again.' },
                }));
              }
            }
          },
          error: (err: unknown) => subscriber.error(err),
        });

      return () => request.unsubscribe();
    });
  }
}
