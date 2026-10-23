import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { ConversationSummary } from '../../../core/models/chat.models';
import { SEARCH_DEBOUNCE_MS, Sidebar, highlightMatches } from './sidebar';

describe('Sidebar', () => {
  let fixture: ComponentFixture<Sidebar>;
  let el: HTMLElement;
  const now = new Date().toISOString();
  const conv = (id: number, title: string, extra: Partial<ConversationSummary> = {}): ConversationSummary => ({
    conversationId: id, title, createdAt: now, updatedAt: now, isPinned: false, archivedAt: null, ...extra,
  });

  beforeEach(async () => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    fixture = TestBed.createComponent(Sidebar);
    fixture.componentRef.setInput('conversations', [conv(1, 'Pinned plan', { isPinned: true }), conv(2, 'Goa trip')]);
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => vi.useRealTimers());

  function type(text: string): void {
    const input = el.querySelector('.search input') as HTMLInputElement;
    input.value = text;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('shows pinned chats in their own section at the top', () => {
    const labels = Array.from(el.querySelectorAll('.group-label')).map((h) => h.textContent);
    expect(labels).toEqual(['Pinned', 'Today']);
    expect(el.querySelector('.row .item')?.textContent).toBe('Pinned plan');
  });

  it('debounces the search box by 300 ms and emits only the last text', () => {
    const searches: string[] = [];
    fixture.componentInstance.search.subscribe((q) => searches.push(q));

    type('go');
    type('goa');
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS - 1);
    expect(searches).toEqual([]);
    vi.advanceTimersByTime(1);
    expect(searches).toEqual(['goa']);

    type('g'); // too short: clears the search
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);
    expect(searches).toEqual(['goa', '']);
  });

  it('shows results with the matched words highlighted', () => {
    type('goa');
    fixture.componentRef.setInput('searchResults', [
      { conversationId: 2, title: 'Goa trip', updatedAt: now, isPinned: false, archivedAt: null, titleMatches: true,
        snippet: '…we fly to GOA on Friday…', messageId: 9 },
    ]);
    fixture.detectChanges();

    const marks = Array.from(el.querySelectorAll('.result mark')).map((m) => m.textContent);
    expect(marks).toEqual(['Goa', 'GOA']);
    expect(el.querySelector('.result-snippet')?.textContent?.replace(/\s+/g, ' ').trim()).toBe('…we fly to GOA on Friday…');
    expect(el.querySelector('.group-label')?.textContent).toBe('Search results');
  });

  it('says when nothing matches, and Escape clears the search', () => {
    const searches: string[] = [];
    fixture.componentInstance.search.subscribe((q) => searches.push(q));
    type('zzz');
    fixture.componentRef.setInput('searchResults', []);
    fixture.detectChanges();
    expect(el.textContent).toContain('No chats match “zzz”.');

    el.querySelector('.search input')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(searches.at(-1)).toBe('');
    expect(el.querySelector('.result')).toBeNull();
  });

  it('pin and archive from the ⋯ menu, and the Archived view can unarchive', () => {
    const pins: boolean[] = [];
    const archives: [number, boolean][] = [];
    fixture.componentInstance.pin.subscribe((r) => pins.push(r.pinned));
    fixture.componentInstance.archive.subscribe((r) => archives.push([r.conversation.conversationId, r.archived]));
    let opened = 0;
    fixture.componentInstance.openArchived.subscribe(() => opened++);

    const rows = el.querySelectorAll('.row');
    (rows[1].querySelector('.more-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    const items = Array.from(el.querySelectorAll('[role="menuitem"]')).map((b) => b.textContent?.trim());
    expect(items).toEqual(['Pin', 'Rename', 'Export as TXT', 'Archive', 'Delete']);
    (el.querySelectorAll('[role="menuitem"]')[0] as HTMLButtonElement).click();
    expect(pins).toEqual([true]);

    (rows[1].querySelector('.more-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    (Array.from(el.querySelectorAll('[role="menuitem"]')).find((b) => b.textContent?.trim() === 'Archive') as HTMLButtonElement).click();
    expect(archives).toEqual([[2, true]]);

    (el.querySelector('.archived-link') as HTMLButtonElement).click();
    fixture.componentRef.setInput('archived', [conv(3, 'Old chat', { archivedAt: now })]);
    fixture.detectChanges();
    expect(opened).toBe(1);
    expect(el.querySelector('.group-label')?.textContent).toBe('Archived');
    (el.querySelector('button[aria-label="Unarchive Old chat"]') as HTMLButtonElement).click();
    expect(archives).toEqual([[2, true], [3, false]]);
  });

  it('highlightMatches marks every case-insensitive occurrence', () => {
    expect(highlightMatches('Goa and goa', 'GOA')).toEqual([
      { text: 'Goa', match: true },
      { text: ' and ', match: false },
      { text: 'goa', match: true },
    ]);
    expect(highlightMatches('plain', '')).toEqual([{ text: 'plain', match: false }]);
  });
});
