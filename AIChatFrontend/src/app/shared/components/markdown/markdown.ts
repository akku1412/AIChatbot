import { Component, ViewEncapsulation, computed, inject, input } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import DOMPurify from 'dompurify';
import hljs from 'highlight.js/lib/core';
import bash from 'highlight.js/lib/languages/bash';
import cpp from 'highlight.js/lib/languages/cpp';
import csharp from 'highlight.js/lib/languages/csharp';
import css from 'highlight.js/lib/languages/css';
import go from 'highlight.js/lib/languages/go';
import java from 'highlight.js/lib/languages/java';
import javascript from 'highlight.js/lib/languages/javascript';
import json from 'highlight.js/lib/languages/json';
import markdownLang from 'highlight.js/lib/languages/markdown';
import powershell from 'highlight.js/lib/languages/powershell';
import python from 'highlight.js/lib/languages/python';
import scss from 'highlight.js/lib/languages/scss';
import sql from 'highlight.js/lib/languages/sql';
import typescript from 'highlight.js/lib/languages/typescript';
import xml from 'highlight.js/lib/languages/xml';
import yaml from 'highlight.js/lib/languages/yaml';
import { Marked, Tokens } from 'marked';

// Only the languages people ask about most, to keep the bundle small (highlight.js core + 16 grammars).
hljs.registerLanguage('csharp', csharp);
hljs.registerLanguage('typescript', typescript);
hljs.registerLanguage('javascript', javascript);
hljs.registerLanguage('sql', sql);
hljs.registerLanguage('python', python);
hljs.registerLanguage('xml', xml); // also html
hljs.registerLanguage('css', css);
hljs.registerLanguage('scss', scss);
hljs.registerLanguage('json', json);
hljs.registerLanguage('bash', bash);
hljs.registerLanguage('powershell', powershell);
hljs.registerLanguage('yaml', yaml);
hljs.registerLanguage('java', java);
hljs.registerLanguage('cpp', cpp);
hljs.registerLanguage('go', go);
hljs.registerLanguage('markdown', markdownLang);

/**
 * Highlighted HTML for a code block. highlight.js escapes the code itself and only adds <span class="hljs-…">;
 * the whole reply still goes through DOMPurify afterwards. Unknown or missing languages are just escaped.
 */
function highlight(code: string, lang: string): string {
  const language = lang && hljs.getLanguage(lang) ? lang : null;
  if (!language) {
    return escapeHtml(code);
  }
  try {
    return hljs.highlight(code, { language, ignoreIllegals: true }).value;
  } catch {
    return escapeHtml(code);
  }
}

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
    // Fenced code: language label + Copy button (handled by click delegation below) + syntax highlighting.
    code(token: Tokens.Code): string {
      const lang = (token.lang ?? '').trim().split(/\s+/)[0]?.replace(/[^\w+#.-]/g, '') ?? '';
      const label = lang || 'code';
      return (
        `<div class="code-block">` +
        `<div class="code-header"><span>${escapeHtml(label)}</span>` +
        `<button type="button" class="copy-code" data-copy-code>Copy</button></div>` +
        `<pre><code class="hljs${lang ? ` language-${escapeHtml(lang)}` : ''}">${highlight(token.text, lang)}</code></pre>` +
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
  host: { class: 'markdown', '[class.streaming]': 'streaming()', '[innerHTML]': 'html()', '(click)': 'onClick($event)' },
  encapsulation: ViewEncapsulation.None,
  styleUrl: './markdown.css',
})
export class MarkdownView {
  private readonly sanitizer = inject(DomSanitizer);

  readonly content = input.required<string>();
  /** The reply is still arriving: shows a blinking caret after the text. */
  readonly streaming = input(false);

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
