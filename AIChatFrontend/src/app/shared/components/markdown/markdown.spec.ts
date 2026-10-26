import { TestBed } from '@angular/core/testing';

import { MessageSource } from '../../../core/models/chat.models';
import { MarkdownView } from './markdown';

describe('MarkdownView', () => {
  async function render(content: string, streaming = false): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(MarkdownView);
    fixture.componentRef.setInput('content', content);
    fixture.componentRef.setInput('streaming', streaming);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('highlights code in known languages, with a language label and Copy button', async () => {
    const el = await render('```csharp\npublic class Order { int Id = 42; }\n```');
    expect(el.querySelector('.code-header span')?.textContent).toBe('csharp');
    expect(el.querySelector('[data-copy-code]')).not.toBeNull();
    expect(el.querySelector('code .hljs-keyword')?.textContent).toBe('public');
    expect(el.querySelector('code .hljs-number')?.textContent).toBe('42');
    expect(el.querySelector('code')?.textContent).toBe('public class Order { int Id = 42; }');
  });

  it('keeps unknown languages as plain escaped text', async () => {
    const el = await render('```brainfuck\n<b>++</b>\n```');
    expect(el.querySelector('code b')).toBeNull();
    expect(el.querySelector('code')?.textContent).toBe('<b>++</b>');
  });

  it('never turns code or raw HTML into live markup', async () => {
    const el = await render('```html\n<img src=x onerror="alert(1)"><script>alert(2)</script>\n```\n\n<img src=x onerror="alert(3)">');
    expect(el.querySelector('img, script')).toBeNull();
    expect(el.querySelector('[onerror]')).toBeNull();
    expect(el.textContent).toContain('<script>alert(2)</script>');
  });

  it('marks a streaming reply (blinking caret)', async () => {
    expect((await render('Hello', true)).classList.contains('streaming')).toBe(true);
    expect((await render('Hello', false)).classList.contains('streaming')).toBe(false);
  });

  describe('citations', () => {
    const page3: MessageSource = { chunkId: 12, fileId: 'f-1', fileName: 'memo.pdf', location: 'Page 3', isOcr: false };
    const slide4: MessageSource = { chunkId: 13, fileId: 'f-2', fileName: 'deck.pptx', location: 'Slide 4', isOcr: false };

    async function renderCited(content: string, sources: MessageSource[]) {
      const fixture = TestBed.createComponent(MarkdownView);
      fixture.componentRef.setInput('content', content);
      fixture.componentRef.setInput('sources', sources);
      const cited: MessageSource[] = [];
      fixture.componentInstance.cite.subscribe((s) => cited.push(s));
      await fixture.whenStable();
      return { el: fixture.nativeElement as HTMLElement, cited };
    }

    it('turns known [file:id] markers into chips that open the source', async () => {
      const { el, cited } = await renderCited('Deadline is Dec 15 [file:12]. See also [file:12, file:13].', [page3, slide4]);
      const chips = Array.from(el.querySelectorAll<HTMLButtonElement>('button.citation'));
      expect(chips.map((c) => c.textContent)).toEqual(['Page 3', 'Page 3', 'Slide 4']);
      expect(chips[0].getAttribute('aria-label')).toBe('Source: memo.pdf, Page 3');
      expect(el.textContent).not.toContain('file:');

      chips[2].click();
      expect(cited).toEqual([slide4]);
    });

    it('drops markers whose ids are not sources (invented ids, or while streaming)', async () => {
      const { el } = await renderCited('Fact [file:999]. Other [file:12, file:999].', [page3]);
      expect(Array.from(el.querySelectorAll('button.citation'), (c) => c.textContent)).toEqual(['Page 3']);
      expect(el.textContent).not.toContain('999');

      const streaming = await renderCited('Fact [file:12]', []);
      expect(streaming.el.querySelector('button.citation')).toBeNull();
      expect(streaming.el.textContent?.trim()).toBe('Fact');
    });

    it('leaves markers inside code alone and escapes file names', async () => {
      const evil: MessageSource = { ...page3, fileName: '<img src=x onerror=alert(1)>.pdf' };
      const { el } = await renderCited('`[file:12]` and [file:12]', [evil]);
      expect(el.querySelector('code')?.textContent).toBe('[file:12]');
      expect(el.querySelector('img')).toBeNull();
      expect(el.querySelector('button.citation')?.getAttribute('title')).toBe('<img src=x onerror=alert(1)>.pdf, Page 3');
    });
  });
});
