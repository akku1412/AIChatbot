import { Injectable, signal } from '@angular/core';

/**
 * "Read aloud" for AI replies with the browser's built-in speech (free, nothing leaves the device).
 * One reply at a time: starting another stops the current one.
 */
@Injectable({ providedIn: 'root' })
export class ReadAloudService {
  /** The message being read, or null. */
  readonly speakingId = signal<number | null>(null);

  get supported(): boolean {
    return typeof window !== 'undefined' && 'speechSynthesis' in window && typeof SpeechSynthesisUtterance !== 'undefined';
  }

  speak(messageId: number, markdown: string): void {
    if (!this.supported) {
      return;
    }
    window.speechSynthesis.cancel();
    const text = speakableText(markdown);
    if (!text) {
      this.speakingId.set(null);
      return;
    }
    const utterance = new SpeechSynthesisUtterance(text);
    utterance.lang = document.documentElement.lang || navigator.language || 'en';
    const done = () => {
      if (this.speakingId() === messageId) {
        this.speakingId.set(null);
      }
    };
    utterance.onend = done;
    utterance.onerror = done;
    this.speakingId.set(messageId);
    window.speechSynthesis.speak(utterance);
  }

  stop(): void {
    if (this.supported) {
      window.speechSynthesis.cancel();
    }
    this.speakingId.set(null);
  }
}

/** Markdown → something pleasant to hear: no symbols, link text without URLs, code blocks skipped. */
export function speakableText(markdown: string): string {
  return markdown
    .replace(/```[\s\S]*?```/g, ' (code block skipped) ')
    .replace(/`([^`]+)`/g, '$1')
    .replace(/!\[[^\]]*\]\([^)]*\)/g, ' ')
    .replace(/\[([^\]]+)\]\(<?[^)>]*>?\)/g, '$1')
    .replace(/\[(?:file|web):[^\]]*\]/gi, ' ')
    .replace(/^\s{0,3}#{1,6}\s+/gm, '')
    .replace(/^\s*>\s?/gm, '')
    .replace(/^\s*[-*+]\s+/gm, '')
    .replace(/^\s*\|?[\s:|-]+\|[\s:|-]*$/gm, ' ')
    .replace(/[|*_~#]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}
