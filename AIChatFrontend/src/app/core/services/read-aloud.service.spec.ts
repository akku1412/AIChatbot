import { TestBed } from '@angular/core/testing';

import { ReadAloudService, speakableText } from './read-aloud.service';

describe('ReadAloudService', () => {
  let spoken: { text: string; onend: (() => void) | null }[];
  let cancel: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    spoken = [];
    cancel = vi.fn();
    vi.stubGlobal('SpeechSynthesisUtterance', class {
      lang = '';
      onend: (() => void) | null = null;
      onerror: (() => void) | null = null;
      constructor(public text: string) {}
    });
    Object.defineProperty(window, 'speechSynthesis', {
      value: { speak: (u: { text: string; onend: (() => void) | null }) => spoken.push(u), cancel },
      configurable: true,
    });
  });

  afterEach(() => vi.unstubAllGlobals());

  it('reads one reply at a time and knows which one', () => {
    const service = TestBed.inject(ReadAloudService);

    service.speak(1, 'First reply');
    expect(service.speakingId()).toBe(1);
    service.speak(2, 'Second reply');
    expect(cancel).toHaveBeenCalledTimes(2); // starting another stops the current one
    expect(service.speakingId()).toBe(2);
    expect(spoken.map((u) => u.text)).toEqual(['First reply', 'Second reply']);

    spoken[0]!.onend?.(); // a stale "end" from the first one doesn't clear the second
    expect(service.speakingId()).toBe(2);
    spoken[1]!.onend?.();
    expect(service.speakingId()).toBeNull();
  });

  it('stop cancels speech', () => {
    const service = TestBed.inject(ReadAloudService);
    service.speak(3, 'Hello');
    service.stop();
    expect(service.speakingId()).toBeNull();
    expect(cancel).toHaveBeenCalled();
  });

  it('reads Markdown as plain words, skipping code and URLs', () => {
    const markdown = [
      '## Totals',
      '',
      'Revenue grew **12%** in `Q3` ([report](<https://example.com/x>)) [file:12] [web:1].',
      '',
      '- one',
      '- two',
      '',
      '```csharp',
      'var x = 1;',
      '```',
      '',
      '| A | B |',
      '|---|---|',
      '| 1 | 2 |',
    ].join('\n');

    expect(speakableText(markdown)).toBe('Totals Revenue grew 12% in Q3 (report) . one two (code block skipped) A B 1 2');
  });
});
