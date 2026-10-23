import { TestBed } from '@angular/core/testing';

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
});
