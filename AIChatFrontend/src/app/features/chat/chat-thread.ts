import { Component, ElementRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { AuthService } from '../../core/auth/auth.service';
import { errorMessage } from '../../core/http/api-error';
import { ChatStore } from '../../core/services/chat.store';
import { ProjectService } from '../../core/services/project.service';
import { SourcePreviewService } from '../../core/services/source-preview.service';
import { AppLogo } from '../../shared/components/app-logo/app-logo';
import { ChatMessageView } from '../../shared/components/chat-message/chat-message';
import { LoadingSpinner } from '../../shared/components/loading-spinner/loading-spinner';
import { ComposerSubmit, MessageInput } from '../../shared/components/message-input/message-input';
import { SourcePanel } from '../../shared/components/source-panel/source-panel';
import { NotificationService } from '../../shared/components/toast/notification.service';
import { TypingIndicator } from '../../shared/components/typing-indicator/typing-indicator';

/** The open conversation (/chat/:conversationId) or a new chat (/chat). */
@Component({
  selector: 'app-chat-thread',
  imports: [RouterLink, AppLogo, ChatMessageView, MessageInput, TypingIndicator, LoadingSpinner, SourcePanel],
  templateUrl: './chat-thread.html',
  styleUrl: './chat-thread.css',
})
export class ChatThread {
  protected readonly store = inject(ChatStore);
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);
  private readonly sourcePreview = inject(SourcePreviewService);
  private readonly projects = inject(ProjectService);

  /** Route param (bound by withComponentInputBinding); undefined on /chat. */
  readonly conversationId = input<string>();

  /** A new chat started from a project: shown on the welcome screen so the user knows where it will go. */
  protected readonly newChatProject = computed(() => {
    const id = this.store.newChatProject();
    return id === null || this.conversationId() !== undefined ? null : (this.projects.projects().find((p) => p.projectId === id) ?? null);
  });

  protected readonly draft = signal('');
  protected readonly suggestions: { icon: 'learn' | 'code' | 'plan' | 'write'; title: string; detail: string; prompt: string }[] = [
    {
      icon: 'learn',
      title: 'Explain a concept',
      detail: 'How JWT refresh tokens work, simply',
      prompt: 'Explain how JWT refresh tokens work, simply, with a short example.',
    },
    {
      icon: 'code',
      title: 'Write some code',
      detail: 'A SQL query that finds duplicate emails',
      prompt: 'Write a SQL Server query that finds duplicate emails in a Users table.',
    },
    {
      icon: 'plan',
      title: 'Make a plan',
      detail: 'A 3-day study plan for Angular signals',
      prompt: 'Give me a 3-day study plan for learning Angular signals.',
    },
    {
      icon: 'write',
      title: 'Draft a message',
      detail: 'A polite follow-up email after an interview',
      prompt: 'Help me write a polite follow-up email after a job interview.',
    },
  ];

  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');
  private readonly composer = viewChild(MessageInput);

  constructor() {
    // Open the conversation named in the URL (or reset to a new chat).
    effect(() => {
      const raw = this.conversationId();
      untracked(() => {
        this.sourcePreview.close(); // a source from the previous chat doesn't belong here
        if (raw === undefined) {
          if (!this.store.sending()) {
            this.store.startNewChat();
          }
          return;
        }
        const id = Number(raw);
        if (Number.isInteger(id) && id > 0) {
          this.store.openConversation(id);
        } else {
          this.router.navigate(['/chat'], { replaceUrl: true });
        }
      });
    });

    // Keep the newest message in view — but while a reply streams, only if the user hasn't scrolled up to read.
    effect(() => {
      this.store.messages();
      const streaming = this.store.streamingMessageId() !== null;
      setTimeout(() => {
        const el = this.scroller()?.nativeElement;
        if (!el) {
          return;
        }
        const nearBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 160;
        if (!streaming || nearBottom) {
          el.scrollTo({ top: el.scrollHeight, behavior: streaming ? 'auto' : 'smooth' });
        }
      });
    });
  }

  /** The last message is a saved question without a reply (generation was stopped before any text). */
  protected readonly unansweredQuestion = computed(() => {
    const last = this.store.messages().at(-1);
    return !this.store.sending() && last?.role === 'user' && last.messageId > 0 ? last : null;
  });

  protected regenerate(messageId: number): void {
    this.store.regenerate(messageId).subscribe({
      error: (err: unknown) => this.notifications.error(`Couldn't regenerate the reply. ${errorMessage(err)}`),
    });
  }

  protected edit(messageId: number, content: string): void {
    this.store.editMessage(messageId, content).subscribe({
      error: (err: unknown) => this.notifications.error(`Your edited message wasn't sent. ${errorMessage(err)}`),
    });
  }

  protected switchVersion(messageId: number, versionNumber: number): void {
    this.store.switchVersion(messageId, versionNumber).subscribe({
      error: (err: unknown) => this.notifications.error(`Couldn't show that version. ${errorMessage(err)}`),
    });
  }

  protected useSuggestion(text: string): void {
    this.draft.set(text);
    this.composer()?.focus();
  }

  protected send({ text, attachments, webSearch }: ComposerSubmit): void {
    this.draft.set('');
    this.store.send(text, attachments, webSearch).subscribe({
      next: (conversationId) => {
        // The files now belong to the sent message. On failure they stay in the composer to retry.
        this.composer()?.clearAttachments();
        if (!this.conversationId()) {
          // First message of a new chat: the conversation now exists, so give it a URL.
          this.router.navigate(['/chat', conversationId], { replaceUrl: true });
        }
      },
      error: (err: unknown) => {
        // Put the text back so nothing the user typed is lost.
        this.draft.set(text);
        this.notifications.error(`Your message wasn't sent. ${errorMessage(err)}`);
      },
    });
  }

  protected firstName(): string {
    return this.auth.user()?.fullName.split(' ')[0] ?? 'there';
  }
}
