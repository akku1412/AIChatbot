import { Component, ViewEncapsulation, computed, inject, input } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import DOMPurify from 'dompurify';
import { Marked, Tokens } from 'marked';

function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

const markdown = new Marked({
  gfm: true,
  breaks: false,
  renderer: {
    // Raw HTML written by the model is shown as text, never interpreted.
    html(token: Tokens.HTML | Tokens.Tag): string {
      return escapeHtml(token.text);
    },
    // Fenced code: language label + Copy button (handled by click delegation below).
    code(token: Tokens.Code): string {
      const lang = (token.lang ?? '').trim().split(/\s+/)[0]?.replace(/[^\w+#.-]/g, '') ?? '';
      const label = lang || 'code';
      return (
        `<div class="code-block">` +
        `<div class="code-header"><span>${escapeHtml(label)}</span>` +
        `<button type="button" class="copy-code" data-copy-code>Copy</button></div>` +
        `<pre><code${lang ? ` class="language-${escapeHtml(lang)}"` : ''}>${escapeHtml(token.text)}</code></pre>` +
        `</div>`
      );
    },
  },
});

// Every link opens in a new tab without giving the new page access to this one.
DOMPurify.addHook('afterSanitizeAttributes', (node) => {
  if (node.tagName === 'A') {
    node.setAttribute('target', '_blank');
    node.setAttribute('rel', 'noopener noreferrer nofollow');
  }
});

const PURIFY_CONFIG = {
  USE_PROFILES: { html: true },
  ADD_ATTR: ['target'],
  // No images (tracking pixels), forms or styles from AI output.
  FORBID_TAGS: ['img', 'style', 'form', 'input', 'textarea', 'select', 'iframe', 'object', 'embed', 'svg', 'math'],
  FORBID_ATTR: ['style'],
};

/**
 * Renders assistant replies as Markdown safely: model HTML is escaped, the output is sanitized with
 * DOMPurify (scripts, event handlers and javascript: URLs removed), and only then trusted by Angular.
 */
@Component({
  selector: 'app-markdown',
  template: '',
  host: { class: 'markdown', '[innerHTML]': 'html()', '(click)': 'onClick($event)' },
  encapsulation: ViewEncapsulation.None,
  styleUrl: './markdown.css',
})
export class MarkdownView {
  private readonly sanitizer = inject(DomSanitizer);

  readonly content = input.required<string>();

  protected readonly html = computed<SafeHtml>(() => {
    const rendered = markdown.parse(this.content(), { async: false });
    const clean = DOMPurify.sanitize(rendered, PURIFY_CONFIG);
    // Safe: `clean` is DOMPurify output. Angular's own sanitizer would strip the code-block buttons.
    return this.sanitizer.bypassSecurityTrustHtml(clean);
  });

  protected async onClick(event: MouseEvent): Promise<void> {
    const button = (event.target as HTMLElement).closest<HTMLButtonElement>('[data-copy-code]');
    const code = button?.closest('.code-block')?.querySelector('code');
    if (!button || !code) {
      return;
    }

    try {
      await navigator.clipboard.writeText(code.textContent ?? '');
      button.textContent = 'Copied';
    } catch {
      button.textContent = 'Copy failed';
    }
    setTimeout(() => (button.textContent = 'Copy'), 2000);
  }
}
