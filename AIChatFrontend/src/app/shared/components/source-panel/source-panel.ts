import { Component, ElementRef, computed, effect, inject, untracked, viewChild } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { catchError, map, of, startWith, switchMap } from 'rxjs';

import { errorMessage } from '../../../core/http/api-error';
import { ChunkPreview } from '../../../core/models/chat.models';
import { FileApiService } from '../../../core/services/file-api.service';
import { SourcePreviewService } from '../../../core/services/source-preview.service';

type PanelState =
  | { status: 'closed' }
  | { status: 'loading' }
  | { status: 'loaded'; chunk: ChunkPreview }
  | { status: 'error'; message: string };

/**
 * Side panel for a cited passage: file name, location (page, slide, sheet) and the passage text, loaded from
 * GET /api/files/{fileId}/chunks/{chunkId}. The text is bound as plain text, never as HTML.
 * Esc or Close dismisses it and returns focus to where the user was.
 */
@Component({
  selector: 'app-source-panel',
  host: { '(document:keydown.escape)': 'close()' },
  template: `
    @if (previews.source(); as source) {
      <aside class="panel" role="dialog" aria-modal="false" aria-labelledby="source-panel-title">
        <header>
          <div class="heading">
            <h2 id="source-panel-title" [title]="source.fileName">{{ source.fileName }}</h2>
            <p class="where">
              {{ source.location }}
              @if (source.isOcr) {
                <span class="tag" title="This text was read from a scanned page or image and may contain recognition errors">Scanned (OCR)</span>
              }
            </p>
          </div>
          <button #closeButton type="button" class="close" aria-label="Close source" (click)="close()">
            <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"/></svg>
          </button>
        </header>
        @switch (state().status) {
          @case ('loading') {
            <p class="note" aria-live="polite">Loading passage…</p>
          }
          @case ('error') {
            <p class="note error" role="alert">{{ errorText() }}</p>
          }
          @case ('loaded') {
            @if (chunk(); as chunk) {
              @if (chunk.isSummary) {
                <p class="note">Summary of the whole file, used to answer questions about it.</p>
              }
              <blockquote class="passage"><mark>{{ chunk.content }}</mark></blockquote>
            }
          }
        }
      </aside>
    }
  `,
  styles: `
    .panel {
      position: fixed; top: 0; right: 0; bottom: 0; z-index: 900; width: min(420px, 100vw);
      display: flex; flex-direction: column; background: var(--surface); border-left: 1px solid var(--border);
      box-shadow: -8px 0 24px rgb(0 0 0 / 0.12);
    }
    header { display: flex; align-items: flex-start; gap: 12px; padding: 16px; border-bottom: 1px solid var(--border); }
    .heading { flex: 1; min-width: 0; }
    h2 { margin: 0; font-size: 1rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .where { margin: 4px 0 0; color: var(--text-muted); font-size: 0.8125rem; display: flex; gap: 8px; align-items: center; }
    .tag { padding: 1px 6px; border-radius: 6px; background: var(--surface-hover); font-size: 0.6875rem; }
    .close {
      flex: none; width: 32px; height: 32px; display: grid; place-items: center; border: 0; border-radius: 8px;
      background: transparent; color: var(--text); cursor: pointer;
    }
    .close:hover { background: var(--surface-hover); }
    .note { margin: 16px; color: var(--text-muted); font-size: 0.875rem; }
    .note.error { color: var(--danger); }
    .passage {
      margin: 16px; padding: 0; overflow: auto; white-space: pre-wrap; overflow-wrap: anywhere;
      font-size: 0.875rem; line-height: 1.6;
    }
    mark { background: color-mix(in srgb, var(--accent) 16%, transparent); color: inherit; border-radius: 3px; padding: 1px 0; }
  `,
})
export class SourcePanel {
  protected readonly previews = inject(SourcePreviewService);
  private readonly fileApi = inject(FileApiService);
  private readonly closeButton = viewChild<ElementRef<HTMLButtonElement>>('closeButton');
  /** Where focus was before the panel opened, to return it on close. */
  private opener: HTMLElement | null = null;

  protected readonly state = toSignal(
    toObservable(this.previews.source).pipe(
      switchMap((source) =>
        !source
          ? of<PanelState>({ status: 'closed' })
          : this.fileApi.getChunk(source.fileId, source.chunkId).pipe(
              map((chunk): PanelState => ({ status: 'loaded', chunk })),
              catchError((err: unknown) => of<PanelState>({ status: 'error', message: errorMessage(err) })),
              startWith<PanelState>({ status: 'loading' }),
            ),
      ),
    ),
    { initialValue: { status: 'closed' } as PanelState },
  );

  protected readonly chunk = computed(() => {
    const state = this.state();
    return state.status === 'loaded' ? state.chunk : null;
  });

  protected readonly errorText = computed(() => {
    const state = this.state();
    return state.status === 'error' ? `Couldn't load this passage. ${state.message}` : '';
  });

  constructor() {
    // Move focus into the panel when it opens (or shows another source).
    effect(() => {
      if (this.previews.source()) {
        untracked(() => {
          const active = document.activeElement as HTMLElement | null;
          if (active && !active.closest('app-source-panel')) {
            this.opener = active;
          }
          setTimeout(() => this.closeButton()?.nativeElement.focus());
        });
      }
    });
  }

  protected close(): void {
    if (!this.previews.source()) {
      return;
    }
    this.previews.close();
    this.opener?.focus();
    this.opener = null;
  }
}
