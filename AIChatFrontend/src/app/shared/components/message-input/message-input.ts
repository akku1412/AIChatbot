import { Component, ElementRef, computed, effect, input, model, output, viewChild } from '@angular/core';

import { MAX_MESSAGE_LENGTH } from '../../../core/models/chat.models';

/**
 * Multiline composer. Enter sends, Shift+Enter inserts a new line, and Enter during IME composition
 * (e.g. Hindi/Japanese keyboards) never sends. Grows with its content up to a max height.
 */
@Component({
  selector: 'app-message-input',
  template: `
    <form class="composer" (submit)="$event.preventDefault(); submit()">
      <textarea
        #box
        rows="1"
        [value]="text()"
        [attr.maxlength]="maxLength"
        [placeholder]="placeholder()"
        [disabled]="disabled()"
        aria-label="Message"
        (input)="onInput($event)"
        (keydown)="onKeydown($event)"
      ></textarea>
      <button type="submit" class="send" [disabled]="!canSend()" [attr.aria-label]="busy() ? 'Sending' : 'Send message'">
        @if (busy()) {
          <span class="stop" aria-hidden="true"></span>
        } @else {
          <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
            <path d="M12 19V5M5 12l7-7 7 7" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" />
          </svg>
        }
      </button>
    </form>
    <p class="hint">
      @if (remaining() < 500) {
        <span [class.over]="remaining() <= 0">{{ remaining() }} characters left · </span>
      }
      <span class="keys">Enter to send · Shift + Enter for a new line</span>
    </p>
  `,
  styles: `
    :host { display: block; }
    .composer {
      display: flex; align-items: flex-end; gap: 8px;
      padding: 8px 8px 8px 16px; border: 1px solid var(--border); border-radius: 24px;
      background: var(--surface); box-shadow: 0 2px 12px rgb(0 0 0 / 0.06);
    }
    .composer:focus-within { border-color: color-mix(in srgb, var(--accent) 60%, var(--border)); }
    textarea {
      flex: 1; resize: none; border: 0; outline: none; background: transparent; color: var(--text);
      font: inherit; line-height: 1.5; padding: 8px 0; max-height: 200px; overflow-y: auto;
    }
    textarea::placeholder { color: var(--text-muted); }
    .send {
      flex: none; width: 36px; height: 36px; border-radius: 50%; border: 0;
      display: grid; place-items: center; cursor: pointer;
      background: var(--text); color: var(--bg);
    }
    .send:disabled { opacity: 0.35; cursor: not-allowed; }
    .stop { width: 12px; height: 12px; border-radius: 2px; background: currentColor; }
    .hint { margin: 6px 0 0; text-align: center; font-size: 0.75rem; color: var(--text-muted); }
    .hint .over { color: var(--danger); }
    @media (max-width: 640px) { .keys { display: none; } }
  `,
})
export class MessageInput {
  /** Two-way bound draft text, so the parent can restore it after a failed send. */
  readonly text = model('');
  readonly disabled = input(false);
  /** True while a reply is being generated: typing is allowed, sending is not. */
  readonly busy = input(false);
  readonly placeholder = input('Message AIChat');
  readonly send = output<string>();

  protected readonly maxLength = MAX_MESSAGE_LENGTH;
  protected readonly remaining = computed(() => MAX_MESSAGE_LENGTH - this.text().length);
  protected readonly canSend = computed(
    () => !this.disabled() && !this.busy() && this.text().trim().length > 0 && this.remaining() >= 0,
  );

  private readonly box = viewChild.required<ElementRef<HTMLTextAreaElement>>('box');

  constructor() {
    // Keep the height in sync when the text changes from outside (cleared or restored).
    effect(() => {
      this.text();
      queueMicrotask(() => this.autosize());
    });
  }

  focus(): void {
    this.box().nativeElement.focus();
  }

  protected onInput(event: Event): void {
    this.text.set((event.target as HTMLTextAreaElement).value);
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      this.submit();
    }
  }

  protected submit(): void {
    if (!this.canSend()) {
      return;
    }
    this.send.emit(this.text().trim());
  }

  private autosize(): void {
    const el = this.box().nativeElement;
    el.style.height = 'auto';
    el.style.height = `${Math.min(el.scrollHeight, 200)}px`;
  }
}
