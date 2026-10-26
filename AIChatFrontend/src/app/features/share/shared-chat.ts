import { DatePipe } from '@angular/common';
import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';

import { errorMessage } from '../../core/http/api-error';
import { SharedConversation } from '../../core/models/chat.models';
import { ChatApiService } from '../../core/services/chat-api.service';
import { AppLogo } from '../../shared/components/app-logo/app-logo';
import { LoadingSpinner } from '../../shared/components/loading-spinner/loading-spinner';
import { MarkdownView } from '../../shared/components/markdown/markdown';

/**
 * /share/:token — a public, read-only copy of a shared chat (no sign-in). Attachments are names only and nothing
 * here can fetch files; the page asks search engines not to index it.
 */
@Component({
  selector: 'app-shared-chat',
  imports: [DatePipe, RouterLink, AppLogo, LoadingSpinner, MarkdownView],
  template: `
    <div class="page">
      <header class="top">
        <a class="brand" routerLink="/login"><app-logo [size]="26" /><span>AIChat</span></a>
        <a class="cta" routerLink="/login">Start your own chat</a>
      </header>

      <main class="column">
        @if (error(); as message) {
          <section class="state" role="alert">
            <h1>This link isn't available</h1>
            <p>{{ message }}</p>
          </section>
        } @else if (chat(); as c) {
          <div class="banner">
            <h1>{{ c.title }}</h1>
            <p>Shared {{ c.sharedAt | date: 'longDate' }} · a read-only copy of a conversation from AIChat.</p>
          </div>

          @for (message of c.messages; track $index) {
            <article class="message" [class.user]="message.role === 'user'" [attr.aria-label]="message.role === 'user' ? 'User' : 'AIChat'">
              @if (message.role === 'user') {
                @if (message.attachments.length) {
                  <ul class="files" aria-label="Attached files">
                    @for (name of message.attachments; track $index) {
                      <li>{{ name }}</li>
                    }
                  </ul>
                }
                @if (message.content) {
                  <div class="bubble">{{ message.content }}</div>
                }
              } @else {
                <app-logo class="avatar" [size]="28" />
                <div class="body">
                  <app-markdown [content]="message.content" />
                  @if (message.isTruncated) {
                    <p class="stopped">(stopped)</p>
                  }
                </div>
              }
            </article>
          }
        } @else {
          <div class="state"><app-loading-spinner label="Loading shared chat" /></div>
        }
      </main>
    </div>
  `,
  styles: `
    :host { display: block; min-height: 100vh; background: var(--bg); color: var(--text); }
    .top {
      position: sticky; top: 0; z-index: 5; display: flex; align-items: center; justify-content: space-between;
      padding: 10px 16px; border-bottom: 1px solid var(--border); background: color-mix(in srgb, var(--bg) 90%, transparent);
      backdrop-filter: blur(8px);
    }
    .brand { display: flex; align-items: center; gap: 8px; color: var(--text); text-decoration: none; font-weight: 700; }
    .cta {
      padding: 8px 14px; border-radius: 10px; background: var(--brand-gradient); color: #fff; text-decoration: none;
      font-weight: 600; font-size: 0.875rem;
    }
    .column { max-width: 780px; margin: 0 auto; padding: 24px 16px 64px; display: flex; flex-direction: column; gap: 20px; }
    .banner h1 { margin: 0 0 4px; font-size: 1.5rem; overflow-wrap: anywhere; }
    .banner p { margin: 0; color: var(--text-muted); font-size: 0.875rem; }
    .message { display: flex; gap: 12px; min-width: 0; }
    .message.user { flex-direction: column; align-items: flex-end; }
    .bubble {
      max-width: 85%; padding: 10px 14px; border-radius: 18px; background: var(--surface-raised);
      white-space: pre-wrap; overflow-wrap: anywhere;
    }
    .files { list-style: none; margin: 0 0 6px; padding: 0; display: flex; flex-wrap: wrap; gap: 6px; justify-content: flex-end; }
    .files li {
      padding: 4px 10px; border: 1px solid var(--border); border-radius: 999px; font-size: 0.8125rem; color: var(--text-muted);
    }
    .avatar { flex: none; margin-top: 2px; }
    .body { flex: 1; min-width: 0; }
    .stopped { margin: 4px 0 0; color: var(--text-muted); font-size: 0.8125rem; }
    .state { display: grid; place-items: center; gap: 8px; padding: 64px 0; text-align: center; }
    .state h1 { margin: 0; font-size: 1.25rem; }
    .state p { margin: 0; color: var(--text-muted); }
    @media (max-width: 560px) { .bubble { max-width: 100%; } }
  `,
})
export class SharedChat {
  private readonly api = inject(ChatApiService);
  private readonly meta = inject(Meta);
  private readonly title = inject(Title);

  /** Route param (bound by withComponentInputBinding). */
  readonly token = input.required<string>();

  protected readonly chat = signal<SharedConversation | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    // Search engines: don't index shared chats (the API also sends X-Robots-Tag).
    this.meta.updateTag({ name: 'robots', content: 'noindex, nofollow' });
    // The token is in the URL: don't send it to sites the shared chat links to.
    this.meta.updateTag({ name: 'referrer', content: 'no-referrer' });
    inject(DestroyRef).onDestroy(() => {
      this.meta.removeTag("name='robots'");
      this.meta.removeTag("name='referrer'");
    });

    effect(() => {
      const token = this.token();
      untracked(() => {
        this.chat.set(null);
        this.error.set(null);
        this.api.getSharedConversation(token).subscribe({
          next: (chat) => {
            this.chat.set(chat);
            this.title.setTitle(`${chat.title} · Shared from AIChat`);
          },
          error: (err: unknown) => this.error.set(errorMessage(err)),
        });
      });
    });
  }
}
