import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject, of, throwError } from 'rxjs';

import { ChatMessage, ChatStreamEvent, ConversationSummary, StreamTurnResult } from '../models/chat.models';
import { ChatApiService } from './chat-api.service';
import { ChatStore, STREAM_RENDER_INTERVAL_MS, sortConversations } from './chat.store';
import { ModelService } from './model.service';
import { ProjectService } from './project.service';

describe('ChatStore', () => {
  let store: ChatStore;
  let stream: Subject<ChatStreamEvent>;
  const projectService = { adjustConversationCount: vi.fn(), reset: vi.fn(), projects: signal([]) };
  const api = {
    streamMessage: vi.fn(),
    regenerateMessage: vi.fn(),
    editMessage: vi.fn(),
    switchVersion: vi.fn(),
    stopGeneration: vi.fn(),
    getMessages: vi.fn(),
    patchConversation: vi.fn(),
    searchConversations: vi.fn(),
    createConversation: vi.fn(),
    listConversations: vi.fn(),
    moveConversation: vi.fn(),
    rateMessage: vi.fn(),
    removeRating: vi.fn(),
    deleteConversation: vi.fn(() => of(undefined)),
  };

  const msg = (messageId: number, role: 'user' | 'assistant', content: string, extra: Partial<ChatMessage> = {}): ChatMessage => ({
    messageId, role, content, createdAt: '2026-10-23T05:00:00Z', tokenUsage: null, attachments: [],
    parentMessageId: null, versionNumber: 1, versionCount: 1, isTruncated: false, ...extra,
  });
  const conv = (id: number, extra: Partial<ConversationSummary> = {}): ConversationSummary => ({
    conversationId: id, title: `Chat ${id}`, createdAt: '', updatedAt: `2026-10-2${id}T00:00:00Z`, isPinned: false, archivedAt: null, ...extra,
  });
  const result = (user: ChatMessage, assistant: ChatMessage | null, extra: Partial<StreamTurnResult> = {}): StreamTurnResult => ({
    userMessage: user, assistantMessage: assistant, conversation: conv(7), stopped: false, truncated: false, replacedMessageId: null, ...extra,
  });

  beforeEach(() => {
    vi.useFakeTimers();
    stream = new Subject<ChatStreamEvent>();
    for (const fn of [api.streamMessage, api.regenerateMessage, api.editMessage]) {
      fn.mockReturnValue(stream.asObservable());
    }
    // The real fallback logic over a fixed list (no HTTP).
    const models = {
      models: signal([
        { id: 'fast', label: 'Fast', description: null, supportsVision: false },
        { id: 'smart', label: 'Smart', description: null, supportsVision: true },
      ]),
      defaultModelId: signal<string | null>('smart'),
      effectiveId: ModelService.prototype.effectiveId,
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: ChatApiService, useValue: api },
        { provide: ModelService, useValue: models },
        { provide: ProjectService, useValue: projectService },
      ],
    });
    store = TestBed.inject(ChatStore);
    store.activeConversationId.set(7);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
  });

  describe('projects', () => {
    const inProject = (id: number, projectId: number, extra: Partial<ConversationSummary> = {}) => conv(id, { projectId, ...extra });

    it('a new chat started in a project is created there and listed in its folder, not the main list', () => {
      store.newChatIn(3);
      expect(store.activeConversationId()).toBeNull();
      api.createConversation.mockReturnValue(of(inProject(9, 3)));

      store.send('hi').subscribe();

      expect(api.createConversation).toHaveBeenCalledWith(undefined, undefined, 3);
      expect(store.conversations().map((c) => c.conversationId)).toEqual([]);
      expect(store.projectConversations()[3]?.map((c) => c.conversationId)).toEqual([9]);
      expect(projectService.adjustConversationCount).toHaveBeenCalledWith(3, 1);
      expect(store.activeConversation()?.conversationId).toBe(9);
    });

    it('the plain New chat goes back to the main list', () => {
      store.newChatIn(3);
      store.newChatIn(null);
      api.createConversation.mockReturnValue(of(conv(10)));
      store.send('hi').subscribe();
      expect(api.createConversation).toHaveBeenCalledWith(undefined, undefined, undefined);
    });

    it('moving a chat puts it in exactly one list and updates both counts', () => {
      api.listConversations.mockReturnValue(of({ items: [inProject(5, 3)], page: 1, pageSize: 100, totalCount: 1, hasMore: false }));
      store.loadProjectConversations(3);
      api.moveConversation.mockReturnValue(of(conv(5, { projectId: null })));

      store.moveToProject(inProject(5, 3), null).subscribe();

      expect(api.moveConversation).toHaveBeenCalledWith(5, null);
      expect(store.projectConversations()[3]).toEqual([]);
      expect(store.conversations().map((c) => c.conversationId)).toEqual([5]);
      expect(projectService.adjustConversationCount).toHaveBeenCalledWith(3, -1);
    });

    it('pinning or renaming a project chat keeps it in its folder; archiving moves it to the archived list', () => {
      api.listConversations.mockReturnValue(of({ items: [inProject(5, 3)], page: 1, pageSize: 100, totalCount: 1, hasMore: false }));
      store.loadProjectConversations(3);

      api.patchConversation.mockReturnValue(of(inProject(5, 3, { isPinned: true })));
      store.setPinned(inProject(5, 3), true).subscribe();
      expect(store.conversations()).toEqual([]);
      expect(store.projectConversations()[3]?.[0]?.isPinned).toBe(true);

      api.patchConversation.mockReturnValue(of(inProject(5, 3, { archivedAt: '2026-10-26T00:00:00Z' })));
      store.setArchived(inProject(5, 3), true).subscribe();
      expect(store.projectConversations()[3]).toEqual([]);
    });
  });

  describe('feedback', () => {
    it('shows the rating on the reply once the server saved it, and can take it back', () => {
      store.messages.set([msg(10, 'user', 'q'), msg(11, 'assistant', 'a')]);
      api.rateMessage.mockReturnValue(of({ rating: 'down', comment: 'too long' }));
      api.removeRating.mockReturnValue(of(undefined));

      store.rateMessage(11, 'down', 'too long').subscribe();
      expect(api.rateMessage).toHaveBeenCalledWith(11, 'down', 'too long');
      expect(store.messages()[1]?.feedback).toBe('down');

      store.rateMessage(11, null).subscribe();
      expect(api.removeRating).toHaveBeenCalledWith(11);
      expect(store.messages()[1]?.feedback).toBeNull();
    });

    it('leaves the reply unchanged when saving fails', () => {
      store.messages.set([msg(11, 'assistant', 'a')]);
      api.rateMessage.mockReturnValue(throwError(() => new Error('offline')));
      store.rateMessage(11, 'up').subscribe({ error: () => undefined });
      expect(store.messages()[0]?.feedback).toBeUndefined();
    });
  });

  describe('model picker', () => {
    it('a new chat remembers the pick and creates the conversation with it', () => {
      store.startNewChat();
      expect(store.activeModelId()).toBe('smart'); // the server default

      store.selectModel('fast').subscribe();
      expect(api.patchConversation).not.toHaveBeenCalled();
      expect(store.activeModelId()).toBe('fast');

      api.createConversation.mockReturnValue(of(conv(9, { modelId: 'fast' })));
      store.send('hi').subscribe();
      expect(api.createConversation).toHaveBeenCalledWith(undefined, 'fast', undefined);
      expect(store.activeModelId()).toBe('fast');
    });

    it('an open chat saves the pick on the server', () => {
      api.patchConversation.mockReturnValue(of(conv(7, { modelId: 'fast' })));

      store.selectModel('fast').subscribe();

      expect(api.patchConversation).toHaveBeenCalledWith(7, { modelId: 'fast' });
      expect(store.activeModelId()).toBe('fast');
    });

    it("shows the default for a chat whose model the server doesn't offer any more", () => {
      api.patchConversation.mockReturnValue(of(conv(7, { modelId: 'retired' })));
      store.selectModel('retired').subscribe();
      expect(store.activeModelId()).toBe('smart');
    });
  });

  it('streams the reply with deltas batched every 50 ms, then swaps in the saved messages', () => {
    const emitted: number[] = [];
    store.send('hello').subscribe((id) => emitted.push(id));

    expect(store.messages().map((m) => [m.role, m.content])).toEqual([['user', 'hello'], ['assistant', '']]);
    expect(store.waitingForFirstToken()).toBe(true);

    stream.next({ type: 'delta', text: 'Hi' });
    stream.next({ type: 'delta', text: ' there' });
    expect(store.messages()[1].content).toBe(''); // not rendered per token
    vi.advanceTimersByTime(STREAM_RENDER_INTERVAL_MS);
    expect(store.messages()[1].content).toBe('Hi there');

    stream.next({ type: 'done', result: result(msg(10, 'user', 'hello'), msg(11, 'assistant', 'Hi there')) });
    stream.complete();
    expect(store.messages().map((m) => m.messageId)).toEqual([10, 11]);
    expect(store.sending()).toBe(false);
    expect(emitted).toEqual([7]);
  });

  it('removes the question again when the stream fails', () => {
    store.messages.set([msg(1, 'user', 'old'), msg(2, 'assistant', 'old reply')]);
    let failed = false;
    store.send('new').subscribe({ error: () => (failed = true) });
    stream.error(new Error('boom'));
    expect(failed).toBe(true);
    expect(store.messages().map((m) => m.messageId)).toEqual([1, 2]);
  });

  it('regenerate shows the new version in place of the old reply', () => {
    store.messages.set([msg(1, 'user', 'q'), msg(2, 'assistant', 'old answer')]);
    store.regenerate(2).subscribe();
    expect(api.regenerateMessage).toHaveBeenCalledWith(2);
    expect(store.messages().map((m) => m.content)).toEqual(['q', '']);

    stream.next({ type: 'done', result: result(msg(1, 'user', 'q'), msg(3, 'assistant', 'new', { versionNumber: 2, versionCount: 2 }), { replacedMessageId: 2 }) });
    expect(store.messages().map((m) => [m.messageId, m.versionNumber, m.versionCount])).toEqual([[1, 1, 1], [3, 2, 2]]);
  });

  it('regenerate keeps the old reply when it fails or is stopped before any text', () => {
    const thread = [msg(1, 'user', 'q'), msg(2, 'assistant', 'old answer')];
    store.messages.set(thread);
    store.regenerate(2).subscribe({ error: () => undefined });
    stream.error(new Error('busy'));
    expect(store.messages()).toEqual(thread);

    stream = new Subject();
    api.regenerateMessage.mockReturnValue(stream.asObservable());
    store.regenerate(2).subscribe();
    stream.next({ type: 'done', result: result(msg(1, 'user', 'q'), null, { stopped: true }) });
    expect(store.messages()).toEqual(thread);
  });

  it('edit replaces the question and what follows with the new branch', () => {
    store.messages.set([msg(1, 'user', 'first'), msg(2, 'assistant', 'a1'), msg(3, 'user', 'second'), msg(4, 'assistant', 'a2')]);
    store.editMessage(1, 'first, edited').subscribe();
    expect(api.editMessage).toHaveBeenCalledWith(1, 'first, edited');
    expect(store.messages().map((m) => m.content)).toEqual(['first, edited', '']);
    expect(store.messages()[0].versionNumber).toBe(2);

    stream.next({ type: 'done', result: result(msg(5, 'user', 'first, edited', { versionNumber: 2, versionCount: 2 }), msg(6, 'assistant', 'a1b')) });
    expect(store.messages().map((m) => m.messageId)).toEqual([5, 6]);
  });

  it('switchVersion shows the thread returned by the server', () => {
    api.switchVersion.mockReturnValue(of({ items: [msg(1, 'user', 'q'), msg(2, 'assistant', 'v1', { versionNumber: 1, versionCount: 2 })], hasMore: false }));
    store.switchVersion(3, 1).subscribe();
    expect(api.switchVersion).toHaveBeenCalledWith(3, 1);
    expect(store.messages().map((m) => m.content)).toEqual(['q', 'v1']);
  });

  it('stop asks the server to stop; if that fails the fetch is aborted and the thread reloaded', () => {
    api.stopGeneration.mockReturnValue(throwError(() => new Error('not found')));
    api.getMessages.mockReturnValue(of({ items: [msg(10, 'user', 'q'), msg(11, 'assistant', 'Part', { isTruncated: true })], hasMore: false }));
    store.send('q').subscribe();
    stream.next({ type: 'delta', text: 'Part' });

    store.stopGeneration();
    expect(api.stopGeneration).toHaveBeenCalledWith(7);
    expect(store.sending()).toBe(false); // aborted
    expect(stream.observed).toBe(false); // the fetch subscription is gone (AbortController fired)
    vi.advanceTimersByTime(1000);
    expect(store.messages().map((m) => [m.messageId, m.isTruncated])).toEqual([[10, false], [11, true]]);
  });

  it('pinning and archiving keep the sidebar lists in the right order', () => {
    store.loadConversations = () => undefined;
    const pinned = { ...conv(1), isPinned: true };
    api.patchConversation.mockReturnValueOnce(of(pinned)).mockReturnValueOnce(of({ ...conv(3), archivedAt: '2026-10-23T00:00:00Z' }));
    // Start with three chats, newest first.
    (store as unknown as { conversationList: { set: (v: ConversationSummary[]) => void } }).conversationList.set([conv(3), conv(2), conv(1)]);

    store.setPinned(conv(1), true).subscribe();
    expect(store.conversations().map((c) => c.conversationId)).toEqual([1, 3, 2]);

    store.setArchived(conv(3), true).subscribe();
    expect(store.conversations().map((c) => c.conversationId)).toEqual([1, 2]);
  });

  it('search needs 2+ characters and cancels the previous request', () => {
    const first = new Subject<never>();
    api.searchConversations.mockReturnValueOnce(first as unknown as Observable<never>).mockReturnValueOnce(of([]));
    store.search('a');
    expect(api.searchConversations).not.toHaveBeenCalled();
    expect(store.searchResults()).toBeNull();

    store.search('go');
    store.search('goa');
    expect(first.observed).toBe(false); // first request cancelled
    expect(store.searchResults()).toEqual([]);
  });

  it('sorts pinned chats first, then by recent activity', () => {
    expect(sortConversations([conv(1), conv(2, { isPinned: true }), conv(3)]).map((c) => c.conversationId)).toEqual([2, 3, 1]);
  });
});
