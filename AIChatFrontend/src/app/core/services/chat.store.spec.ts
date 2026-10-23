import { HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ChatMessage } from '../models/chat.models';
import { ChatStore } from './chat.store';

describe('ChatStore streaming, regenerate and stop', () => {
  let store: ChatStore;
  let http: HttpTestingController;

  const msg = (messageId: number, role: 'user' | 'assistant', content: string): ChatMessage => ({
    messageId, role, content, createdAt: '2026-10-22T05:00:00Z', tokenUsage: null, attachments: [],
  });
  const conversation = { conversationId: 7, title: 'Chat', createdAt: '', updatedAt: '' };
  const sse = (event: string, data: object) => `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;
  const progress = (req: TestRequest, text: string) =>
    req.event({ type: HttpEventType.DownloadProgress, loaded: text.length, partialText: text });

  function openWith(messages: ChatMessage[]): void {
    store.activeConversationId.set(7);
    store.messages.set(messages);
  }

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    store = TestBed.inject(ChatStore);
    http = TestBed.inject(HttpTestingController);
  });

  it('streams the reply into the thread, then swaps in the saved messages', () => {
    openWith([]);
    const emitted: number[] = [];
    store.send('hello').subscribe((id) => emitted.push(id));

    expect(store.sending()).toBe(true);
    expect(store.waitingForFirstToken()).toBe(true);
    expect(store.messages().map((m) => [m.role, m.content])).toEqual([['user', 'hello'], ['assistant', '']]);

    const req = http.expectOne((r) => r.url.endsWith('/chat/7/messages/stream'));
    const first = sse('delta', { text: 'Hi' });
    progress(req, first);
    expect(store.waitingForFirstToken()).toBe(false);
    expect(store.messages()[1].content).toBe('Hi');

    const second = first + sse('delta', { text: ' there' });
    progress(req, second);
    expect(store.messages()[1].content).toBe('Hi there');
    expect(store.streamingMessageId()).toBe(store.messages()[1].messageId);

    req.flush(second + sse('done', {
      userMessage: msg(10, 'user', 'hello'), assistantMessage: msg(11, 'assistant', 'Hi there'),
      conversation: { ...conversation, title: 'Greeting' }, stopped: false, truncated: false, replacedMessageId: null,
    }));

    expect(store.messages().map((m) => m.messageId)).toEqual([10, 11]);
    expect(store.sending()).toBe(false);
    expect(store.streamingMessageId()).toBeNull();
    expect(store.conversations()[0].title).toBe('Greeting');
    expect(emitted).toEqual([7]);
  });

  it('removes the question again when the stream fails', () => {
    openWith([msg(1, 'user', 'old'), msg(2, 'assistant', 'old reply')]);
    let failed = false;
    store.send('new').subscribe({ error: () => (failed = true) });
    http.expectOne((r) => r.url.endsWith('/stream')).flush(sse('error', { status: 502, title: 'AI service error', detail: 'x' }));

    expect(failed).toBe(true);
    expect(store.messages().map((m) => m.messageId)).toEqual([1, 2]);
    expect(store.sending()).toBe(false);
  });

  it('regenerate replaces the last reply', () => {
    openWith([msg(1, 'user', 'q'), msg(2, 'assistant', 'old answer')]);
    store.regenerate().subscribe();

    expect(store.messages().map((m) => m.content)).toEqual(['q', '']); // old reply hidden while the new one streams
    http.expectOne((r) => r.url.endsWith('/chat/7/messages/regenerate')).flush(
      sse('delta', { text: 'new answer' }) +
        sse('done', {
          userMessage: msg(1, 'user', 'q'), assistantMessage: msg(3, 'assistant', 'new answer'),
          conversation, stopped: false, truncated: false, replacedMessageId: 2,
        }),
    );
    expect(store.messages().map((m) => [m.messageId, m.content])).toEqual([[1, 'q'], [3, 'new answer']]);
  });

  it('regenerate restores the old reply when it fails', async () => {
    openWith([msg(1, 'user', 'q'), msg(2, 'assistant', 'old answer')]);
    store.regenerate().subscribe({ error: () => undefined });
    http.expectOne((r) => r.url.endsWith('/regenerate')).flush(JSON.stringify({ title: 'AI is busy' }), { status: 429, statusText: 'Too Many' });
    // The error body is parsed asynchronously before the error reaches the store.
    await vi.waitFor(() => expect(store.sending()).toBe(false));
    expect(store.messages().map((m) => m.messageId)).toEqual([1, 2]);
  });

  it('stop asks the server to stop; the stopped "done" keeps the partial reply', () => {
    openWith([]);
    store.send('long please').subscribe();
    const stream = http.expectOne((r) => r.url.endsWith('/stream'));
    progress(stream, sse('delta', { text: 'Part' }));

    store.stopGeneration();
    expect(store.stopping()).toBe(true);
    http.expectOne((r) => r.url.endsWith('/chat/7/messages/stop') && r.method === 'POST').flush(null, { status: 204, statusText: 'No Content' });

    stream.flush(sse('delta', { text: 'Part' }) + sse('done', {
      userMessage: msg(20, 'user', 'long please'), assistantMessage: msg(21, 'assistant', 'Part'),
      conversation, stopped: true, truncated: false, replacedMessageId: null,
    }));
    expect(store.messages().map((m) => [m.messageId, m.content])).toEqual([[20, 'long please'], [21, 'Part']]);
    expect(store.sending()).toBe(false);
    expect(store.stopping()).toBe(false);
  });
});
