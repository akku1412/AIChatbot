import { Component, ElementRef, inject, input, output, signal } from '@angular/core';

import { CHAT_EXPORT_LABELS, ChatExportFormat } from '../../../core/models/chat.models';

/**
 * Top bar of the chat area. The menu and new-chat buttons only show on small screens; export shows for an open chat.
 * Content marked [header-start] (the model picker) goes before the title.
 */
@Component({
  selector: 'app-header',
  host: { '(document:click)': 'onDocumentClick($event)', '(document:keydown.escape)': 'exportMenuOpen.set(false)' },
  template: `
    <header class="header">
      <button type="button" class="icon-button mobile" aria-label="Open sidebar" (click)="menu.emit()">
        <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true"><path d="M4 7h16M4 12h16M4 17h16" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>
      </button>
      <ng-content select="[header-start]" />
      <h1 class="title" [title]="title()">{{ title() }}</h1>
      @if (canExport()) {
        <button type="button" class="icon-button share" aria-label="Share chat" title="Share chat" (click)="share.emit()">
          <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true"><path d="M12 15V4M8 8l4-4 4 4M5 13v6a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>
        </button>
        <div class="export export-wrap">
          <button type="button" class="icon-button" [disabled]="exporting()" aria-label="Export chat" title="Export chat"
                  aria-haspopup="menu" [attr.aria-expanded]="exportMenuOpen()" (click)="exportMenuOpen.set(!exportMenuOpen())">
            <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true"><path d="M12 4v11M7 10l5 5 5-5M5 20h14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>
          </button>
          @if (exportMenuOpen()) {
            <div class="export-menu" role="menu" aria-label="Export chat as">
              @for (format of exportFormats; track format) {
                <button type="button" role="menuitem" (click)="chooseExport(format)">{{ exportLabels[format] }}</button>
              }
            </div>
          }
        </div>
      }
      <button type="button" class="icon-button mobile" aria-label="New chat" (click)="newChat.emit()">
        <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true"><path d="M12 5v14M5 12h14" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>
      </button>
    </header>
  `,
  styles: `
    .header {
      height: 56px; display: flex; align-items: center; gap: 8px; padding: 0 12px;
      border-bottom: 1px solid color-mix(in srgb, var(--border) 60%, transparent); background: color-mix(in srgb, var(--bg) 85%, transparent); backdrop-filter: blur(8px);
    }
    .title {
      flex: 1; margin: 0; font-size: 1rem; font-weight: 600; text-align: center;
      white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
    }
    .icon-button {
      width: 36px; height: 36px; display: grid; place-items: center; border: 0; border-radius: 8px;
      background: transparent; color: var(--text); cursor: pointer;
    }
    .icon-button:hover { background: var(--surface-hover); }
    .icon-button:disabled { opacity: 0.5; cursor: progress; }
    .export, .share { color: var(--text-muted); }
    .export-wrap { position: relative; }
    .export-menu {
      position: absolute; top: calc(100% + 4px); right: 0; z-index: 50; min-width: 160px; padding: 6px;
      border: 1px solid var(--border); border-radius: 10px; background: var(--surface-raised); box-shadow: var(--shadow-md);
    }
    .export-menu button {
      width: 100%; padding: 8px 10px; border: 0; border-radius: 6px; background: transparent; color: var(--text);
      font: inherit; font-size: 0.875rem; text-align: left; cursor: pointer;
    }
    .export-menu button:hover, .export-menu button:focus-visible { background: var(--surface-hover); }
    .export:hover, .share:hover { color: var(--text); }
    .mobile { visibility: hidden; }
    @media (max-width: 768px) { .mobile { visibility: visible; } }
    /* Desktop: the hidden new-chat button would leave a gap after the export button. */
    @media (min-width: 769px) { .export + .mobile { display: none; } }
  `,
})
export class AppHeader {
  readonly title = input('AIChat');
  /** Shows the Share and Export buttons (only when a saved conversation is open). */
  readonly canExport = input(false);
  readonly exporting = input(false);
  readonly menu = output<void>();
  readonly newChat = output<void>();
  /** Export the whole chat in the chosen format. */
  readonly export = output<ChatExportFormat>();
  readonly share = output<void>();

  protected readonly exportFormats: ChatExportFormat[] = ['txt', 'pdf', 'docx'];
  protected readonly exportLabels = CHAT_EXPORT_LABELS;
  protected readonly exportMenuOpen = signal(false);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected chooseExport(format: ChatExportFormat): void {
    this.exportMenuOpen.set(false);
    this.export.emit(format);
  }

  protected onDocumentClick(event: MouseEvent): void {
    if (this.exportMenuOpen() && !this.host.nativeElement.querySelector('.export-wrap')?.contains(event.target as Node)) {
      this.exportMenuOpen.set(false);
    }
  }
}
