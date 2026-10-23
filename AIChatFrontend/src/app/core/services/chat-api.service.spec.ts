import { HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { errorMessage } from '../http/api-error';
import { ChatStreamEvent } from '../models/chat.models';
import { ChatApiService } from './chat-api.service';

describe('ChatApiService streaming', () => {
  let api: ChatApiService;
  let http: HttpTestingController;

  const done = {
    userMessage: { messageId: 1, role: 'user', content: 'hi', createdAt: '', tokenUsage: null, attachments: [] },
    assistantMessage: { messageId: 2, role: 'assistant', content: 'Hello there', createdAt: '', tokenUsage: 5, attachments: [] },
    conversation: { conversationId: 7, title: 'Greeting', createdAt: '', updatedAt: '' },
    stopped: false,
    truncated: false,
    replacedMessageId: null,
  };
  const sse = (event: string, data: object) => `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(ChatApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('emits deltas as partial text arrives (even split mid-event), then done', () => {
    const events: ChatStreamEvent[] = [];
    let completed = false;
    api.streamMessage(7, 'hi').subscribe({ next: (e) => events.push(e), complete: () => (completed = true) });

    const req = http.expectOne((r) => r.url.endsWith('/chat/7/messages/stream'));
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ content: 'hi', attachmentIds: [] });
    expect(req.request.responseType).toBe('text');

    const full = sse('delta', { text: 'Hello' }) + sse('delta', { text: ' there' }) + sse('done', done);
    const cut = full.indexOf(' there') - 3; // in the middle of the second event
    req.event({ type: HttpEventType.DownloadProgress, loaded: cut, partialText: full.slice(0, cut) });
    expect(events).toEqual([{ type: 'delta', text: 'Hello' }]);

    req.flush(full);
    expect(events).toEqual([
      { type: 'delta', text: 'Hello' },
      { type: 'delta', text: ' there' },
      { type: 'done', result: done },
    ]);
    expect(completed).toBe(true);
  });

  it('turns an "error" event into an error with the server message', () => {
    let error: unknown;
    api.streamMessage(7, 'hi').subscribe({ error: (e: unknown) => (error = e) });
    http.expectOne((r) => r.url.endsWith('/stream')).flush(
      sse('delta', { text: 'Par' }) + sse('error', { status: 504, title: 'AI took too long', detail: 'The AI took too long to respond.' }),
    );
    expect(errorMessage(error)).toBe('The AI took too long to respond.');
  });

  it('reports a stream that ends without "done" as interrupted', () => {
    let error: unknown;
    api.regenerate(7).subscribe({ error: (e: unknown) => (error = e) });
    http.expectOne((r) => r.url.endsWith('/chat/7/messages/regenerate')).flush(sse('delta', { text: 'Half' }));
    expect(errorMessage(error)).toContain('connection was lost');
  });

  it('shows the ProblemDetails of an HTTP error that happens before streaming starts', async () => {
    let error: unknown;
    api.streamMessage(7, 'hi').subscribe({ error: (e: unknown) => (error = e) });
    http.expectOne((r) => r.url.endsWith('/stream')).flush(
      JSON.stringify({ title: 'Reply in progress', detail: 'A reply is already being generated in this conversation.' }),
      { status: 409, statusText: 'Conflict' },
    );
    await vi.waitFor(() => expect(error).toBeDefined());
    expect(errorMessage(error)).toBe('A reply is already being generated in this conversation.');
  });

  it('unsubscribing aborts the request', () => {
    const subscription = api.streamMessage(7, 'hi').subscribe();
    const req = http.expectOne((r) => r.url.endsWith('/stream'));
    subscription.unsubscribe();
    expect(req.cancelled).toBe(true);
  });
});
