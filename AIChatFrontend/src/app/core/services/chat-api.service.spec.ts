import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { AuthService } from '../auth/auth.service';
import { errorMessage } from '../http/api-error';
import { ChatStreamEvent } from '../models/chat.models';
import { ChatApiService } from './chat-api.service';

/** A fetch Response whose body arrives in the given chunks (like a real network stream). */
function streamResponse(chunks: string[], status = 200, contentType = 'text/event-stream'): Response {
  const encoder = new TextEncoder();
  const body = new ReadableStream<Uint8Array>({
    start(controller) {
      chunks.forEach((chunk) => controller.enqueue(encoder.encode(chunk)));
      controller.close();
    },
  });
  return new Response(body, { status, headers: { 'Content-Type': contentType } });
}

const sse = (event: string, data: object) => `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;

const done = {
  userMessage: { messageId: 1, role: 'user', content: 'hi', createdAt: '', tokenUsage: null, attachments: [], parentMessageId: null, versionNumber: 1, versionCount: 1, isTruncated: false },
  assistantMessage: { messageId: 2, role: 'assistant', content: 'Hello there', createdAt: '', tokenUsage: 5, attachments: [], parentMessageId: 1, versionNumber: 1, versionCount: 1, isTruncated: false },
  conversation: { conversationId: 7, title: 'Greeting', createdAt: '', updatedAt: '', isPinned: false, archivedAt: null },
  stopped: false,
  truncated: false,
  replacedMessageId: null,
};

describe('ChatApiService', () => {
  let api: ChatApiService;
  let http: HttpTestingController;
  let fetchMock: ReturnType<typeof vi.fn>;
  let token: string;
  const auth = {
    ensureSession: vi.fn(() => of(true)),
    getAccessToken: vi.fn(() => token),
    refresh: vi.fn(() => {
      token = 'fresh-token';
      return of(token);
    }),
    handleSessionExpired: vi.fn(),
  };

  beforeEach(() => {
    token = 'access-token';
    fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: AuthService, useValue: auth }],
    });
    api = TestBed.inject(ChatApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.clearAllMocks();
    http.verify();
  });

  function collect(events$: ReturnType<ChatApiService['streamMessage']>) {
    const events: ChatStreamEvent[] = [];
    const state: { error?: unknown; completed: boolean } = { completed: false };
    const subscription = events$.subscribe({
      next: (e) => events.push(e),
      error: (e: unknown) => (state.error = e),
      complete: () => (state.completed = true),
    });
    return { events, state, subscription };
  }

  // ---------- Streaming with fetch + ReadableStream ----------

  it('streams with fetch: bearer token, deltas (even split mid-event), then done', async () => {
    const full = sse('delta', { text: 'Hello' }) + sse('delta', { text: ' there' }) + sse('done', done);
    const cut = full.indexOf(' there') - 3;
    fetchMock.mockResolvedValue(streamResponse([full.slice(0, cut), full.slice(cut)]));

    const { events, state } = collect(api.streamMessage(7, 'hi'));
    await vi.waitFor(() => expect(state.completed).toBe(true));

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('/api/chat/7/messages/stream');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual({ content: 'hi', attachmentIds: [], webSearch: false });
    expect((init.headers as Record<string, string>)['Authorization']).toBe('Bearer access-token');
    expect(init.signal).toBeInstanceOf(AbortSignal);
    expect(events).toEqual([
      { type: 'delta', text: 'Hello' },
      { type: 'delta', text: ' there' },
      { type: 'done', result: done },
    ]);
  });

  it('regenerate and edit use the message endpoints', async () => {
    fetchMock.mockImplementation(() => Promise.resolve(streamResponse([sse('done', done)])));
    const regenerate = collect(api.regenerateMessage(42));
    await vi.waitFor(() => expect(regenerate.state.completed).toBe(true));
    const edit = collect(api.editMessage(41, 'new text'));
    await vi.waitFor(() => expect(edit.state.completed).toBe(true));
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(fetchMock.mock.calls[0][0]).toBe('/api/messages/42/regenerate');
    expect(fetchMock.mock.calls[1][0]).toBe('/api/messages/41');
    expect((fetchMock.mock.calls[1][1] as RequestInit).method).toBe('PUT');
    expect(JSON.parse((fetchMock.mock.calls[1][1] as RequestInit).body as string)).toEqual({ content: 'new text' });
  });

  it('turns an "error" event into an error with the server message', async () => {
    fetchMock.mockResolvedValue(streamResponse([sse('delta', { text: 'Par' }), sse('error', { status: 504, title: 'AI took too long', detail: 'The AI took too long to respond.' })]));
    const { events, state } = collect(api.streamMessage(7, 'hi'));
    await vi.waitFor(() => expect(state.error).toBeDefined());
    expect(events).toEqual([{ type: 'delta', text: 'Par' }]);
    expect(errorMessage(state.error)).toBe('The AI took too long to respond.');
  });

  it('reports an HTTP error before streaming with the ProblemDetails message', async () => {
    fetchMock.mockResolvedValue(
      streamResponse([JSON.stringify({ title: 'Reply in progress', detail: 'A reply is already being generated in this conversation.' })], 409, 'application/problem+json'),
    );
    const { state } = collect(api.streamMessage(7, 'hi'));
    await vi.waitFor(() => expect(state.error).toBeDefined());
    expect(errorMessage(state.error)).toBe('A reply is already being generated in this conversation.');
  });

  it('refreshes the token once on 401 and retries', async () => {
    fetchMock
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(streamResponse([sse('done', done)]));
    const { state } = collect(api.streamMessage(7, 'hi'));
    await vi.waitFor(() => expect(state.completed).toBe(true));
    expect(auth.refresh).toHaveBeenCalledTimes(1);
    expect(((fetchMock.mock.calls[1][1] as RequestInit).headers as Record<string, string>)['Authorization']).toBe('Bearer fresh-token');
  });

  it('reports a stream that ends without "done" as interrupted', async () => {
    fetchMock.mockResolvedValue(streamResponse([sse('delta', { text: 'Half' })]));
    const { state } = collect(api.streamMessage(7, 'hi'));
    await vi.waitFor(() => expect(state.error).toBeDefined());
    expect(errorMessage(state.error)).toContain('connection was lost');
  });

  it('unsubscribing aborts the fetch (AbortController)', async () => {
    let signal: AbortSignal | undefined;
    fetchMock.mockImplementation((_url: string, init: RequestInit) => {
      signal = init.signal!;
      return new Promise(() => undefined); // never resolves: the reply is still being generated
    });
    const { subscription, state } = collect(api.streamMessage(7, 'hi'));
    await vi.waitFor(() => expect(signal).toBeDefined());
    subscription.unsubscribe();
    expect(signal!.aborted).toBe(true);
    expect(state.error).toBeUndefined();
  });

  // ---------- Regular HTTP calls ----------

  it('switches versions, searches and patches through the API', () => {
    api.switchVersion(5, 2).subscribe();
    const switchReq = http.expectOne('/api/messages/5/switch-version');
    expect(switchReq.request.method).toBe('POST');
    expect(switchReq.request.body).toEqual({ versionNumber: 2 });
    switchReq.flush({ items: [], hasMore: false });

    api.searchConversations('goa trip').subscribe();
    const search = http.expectOne((r) => r.url === '/api/conversations/search');
    expect(search.request.params.get('q')).toBe('goa trip');
    search.flush([]);

    api.patchConversation(3, { isPinned: true }).subscribe();
    const patch = http.expectOne('/api/conversations/3');
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.body).toEqual({ isPinned: true });
    patch.flush({});

    api.listConversations(1, 20, true).subscribe();
    const list = http.expectOne((r) => r.url === '/api/conversations');
    expect(list.request.params.get('archived')).toBe('true');
    list.flush({ items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false });
  });
});
