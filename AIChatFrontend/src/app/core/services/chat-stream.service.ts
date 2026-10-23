import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';

import { AuthService } from '../auth/auth.service';
import { ChatStreamEvent, StreamTurnResult } from '../models/chat.models';
import { SseParser } from './sse-parser';

/**
 * Reads the API's Server-Sent Events with fetch + ReadableStream. EventSource can't send the Authorization header,
 * and fetch lets the stream be read piece by piece and cancelled with an AbortController.
 *
 * - Auth: uses the current access token (refreshed first if it is about to expire); on a 401 it refreshes once
 *   and retries, like the HTTP interceptor does for normal calls.
 * - Unsubscribing aborts the request; the server then saves whatever was generated (marked as stopped).
 * - Errors are HttpErrorResponse objects carrying the server's ProblemDetails, so errorMessage() works as usual.
 */
@Injectable({ providedIn: 'root' })
export class ChatStreamService {
  private readonly auth = inject(AuthService);

  stream(url: string, method: 'POST' | 'PUT', body: object): Observable<ChatStreamEvent> {
    return new Observable<ChatStreamEvent>((subscriber) => {
      const controller = new AbortController();
      let finished = false;

      const open = async (retried: boolean): Promise<Response> => {
        await firstValueFrom(this.auth.ensureSession());
        const token = this.auth.getAccessToken();
        const response = await fetch(url, {
          method,
          headers: {
            'Content-Type': 'application/json',
            Accept: 'text/event-stream',
            ...(token ? { Authorization: `Bearer ${token}` } : {}),
          },
          body: JSON.stringify(body),
          credentials: 'same-origin',
          signal: controller.signal,
        });
        if (response.status === 401 && !retried) {
          const refreshed = await firstValueFrom(this.auth.refresh());
          if (refreshed) {
            return open(true);
          }
          this.auth.handleSessionExpired();
        }
        return response;
      };

      const run = async (): Promise<void> => {
        const response = await open(false);
        if (!response.ok || !response.body) {
          throw await toHttpError(response, url);
        }

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        const parser = new SseParser();
        while (!subscriber.closed) {
          const { value, done } = await reader.read();
          const text = done ? decoder.decode() : decoder.decode(value, { stream: true });
          for (const event of parser.push(text)) {
            const data = JSON.parse(event.data) as Record<string, unknown>;
            if (event.event === 'delta') {
              subscriber.next({ type: 'delta', text: String(data['text'] ?? '') });
            } else if (event.event === 'done') {
              finished = true;
              subscriber.next({ type: 'done', result: data as unknown as StreamTurnResult });
            } else if (event.event === 'error') {
              throw new HttpErrorResponse({ status: Number(data['status']) || 500, error: data, url });
            }
          }
          if (done) {
            break;
          }
        }

        if (!finished) {
          throw new HttpErrorResponse({
            status: 0,
            url,
            error: { title: 'Reply interrupted', detail: 'The connection was lost before the reply finished. Please try again.' },
          });
        }
        subscriber.complete();
      };

      run().catch((err: unknown) => {
        if (controller.signal.aborted || subscriber.closed) {
          return; // cancelled on purpose (Stop, navigation, unsubscribe)
        }
        subscriber.error(
          err instanceof HttpErrorResponse
            ? err
            : // fetch() rejects with a TypeError when the server can't be reached.
              new HttpErrorResponse({ status: 0, url, error: err }),
        );
      });

      return () => controller.abort();
    });
  }
}

/** A failed response before streaming started: the body is the server's ProblemDetails JSON (or nothing). */
async function toHttpError(response: Response, url: string): Promise<HttpErrorResponse> {
  let error: unknown = null;
  try {
    const text = await response.text();
    error = text ? JSON.parse(text) : null;
  } catch {
    error = null;
  }
  const headers = new HttpHeaders(Object.fromEntries(response.headers.entries()));
  return new HttpErrorResponse({ status: response.status, statusText: response.statusText, error, headers, url });
}
