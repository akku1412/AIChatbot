import { TestBed } from '@angular/core/testing';

import { MarkdownView } from './markdown';

async function render(content: string): Promise<HTMLElement> {
  const fixture = TestBed.createComponent(MarkdownView);
  fixture.componentRef.setInput('content', content);
  await fixture.whenStable();
  return fixture.nativeElement as HTMLElement;
}

describe('MarkdownView', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [MarkdownView] }).compileComponents();
  });

  it('renders Markdown formatting', async () => {
    const el = await render('# Title\n\n**bold** and `code`\n\n- one\n- two');
    expect(el.querySelector('h1')?.textContent).toBe('Title');
    expect(el.querySelector('strong')?.textContent).toBe('bold');
    expect(el.querySelectorAll('li').length).toBe(2);
  });

  it('renders fenced code with a language label and copy button, escaped', async () => {
    const el = await render('```html\n<b onclick="x()">hi</b>\n```');
    expect(el.querySelector('.code-header span')?.textContent).toBe('html');
    expect(el.querySelector('[data-copy-code]')).not.toBeNull();
    expect(el.querySelector('code')?.textContent).toContain('<b onclick="x()">hi</b>');
    expect(el.querySelector('code b')).toBeNull();
  });

  it('never executes raw HTML from the model', async () => {
    const el = await render('<script>alert(1)</script><img src=x onerror="alert(1)"><b>raw</b>');
    expect(el.querySelector('script')).toBeNull();
    expect(el.querySelector('img')).toBeNull();
    expect(el.querySelector('b')).toBeNull();
    expect(el.textContent).toContain('<script>');
  });

  it('removes javascript: links and hardens normal links', async () => {
    const el = await render('[bad](javascript:alert(1)) [good](https://angular.dev)');
    const links = Array.from(el.querySelectorAll('a'));
    expect(links.some((a) => (a.getAttribute('href') ?? '').startsWith('javascript:'))).toBe(false);
    const good = links.find((a) => a.getAttribute('href') === 'https://angular.dev');
    expect(good?.getAttribute('target')).toBe('_blank');
    expect(good?.getAttribute('rel')).toContain('noopener');
  });

  it('does not render images (no tracking pixels)', async () => {
    const el = await render('![pixel](https://tracker.example/p.gif)');
    expect(el.querySelector('img')).toBeNull();
  });
});
