import { Component, computed, input, output } from '@angular/core';

export type ChipState = 'uploading' | 'processing' | 'ready' | 'error' | 'sent';

/** Human-readable size: 532 B, 14.2 KB, 3.4 MB. */
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(1)} KB`;
  }
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/**
 * One file as a compact card: type badge, name, size or status, optional progress bar and remove button.
 * Used in the composer (pending uploads) and on sent messages.
 */
@Component({
  selector: 'app-attachment-chip',
  template: `
    <div class="chip" [class]="state()" [attr.title]="error() ?? fileName()">
      @if (thumbnailUrl(); as src) {
        <img class="thumb" [src]="src" alt="" />
      } @else {
        <span class="badge" [class]="kind()" aria-hidden="true">{{ badge() }}</span>
      }
      <span class="text">
        <span class="name">{{ fileName() }}</span>
        <span class="meta" [class.error-text]="state() === 'error'">{{ meta() }}</span>
      </span>
      @if (downloadable()) {
        <button type="button" class="download" [disabled]="downloading()" [attr.aria-busy]="downloading()"
                [attr.aria-label]="(downloading() ? 'Downloading ' : 'Download ') + fileName()"
                [title]="downloading() ? 'Downloading…' : 'Download original file'" (click)="download.emit()">
          @if (downloading()) {
            <span class="spinner" aria-hidden="true"></span>
          } @else {
            <svg viewBox="0 0 24 24" width="15" height="15" aria-hidden="true"><path d="M12 4v11M7 10l5 5 5-5M5 20h14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>
          }
        </button>
      }
      @if (removable()) {
        <button type="button" class="remove" [attr.aria-label]="'Remove ' + fileName()" (click)="remove.emit()">
          <svg viewBox="0 0 24 24" width="14" height="14" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"/></svg>
        </button>
      }
      @if (state() === 'uploading') {
        <span class="progress" role="progressbar" [attr.aria-valuenow]="progress()" aria-valuemin="0" aria-valuemax="100"
              [attr.aria-label]="'Uploading ' + fileName()">
          <span class="bar" [style.width.%]="progress()"></span>
        </span>
      }
      @if (state() === 'processing') {
        @if (progress() > 0) {
          <span class="progress" role="progressbar" [attr.aria-valuenow]="progress()" aria-valuemin="0" aria-valuemax="100"
                [attr.aria-label]="'Reading ' + fileName()">
            <span class="bar" [style.width.%]="progress()"></span>
          </span>
        } @else {
          <span class="progress indeterminate" aria-hidden="true"><span class="bar"></span></span>
        }
      }
    </div>
  `,
  styles: `
    :host { display: inline-flex; min-width: 0; max-width: 100%; }
    .chip {
      position: relative; display: flex; align-items: center; gap: 10px; min-width: 0; width: 240px; max-width: 100%;
      padding: 8px 10px; border: 1px solid var(--border); border-radius: 12px; background: var(--surface-raised);
      overflow: hidden;
    }
    .chip.error { border-color: color-mix(in srgb, var(--danger) 55%, var(--border)); }
    .chip.sent { width: auto; max-width: 280px; background: var(--surface); }
    .badge {
      flex: none; width: 34px; height: 34px; border-radius: 8px; display: grid; place-items: center;
      color: #fff; font-size: 0.625rem; font-weight: 800; letter-spacing: 0.02em;
    }
    .badge.pdf { background: #dc2626; }
    .badge.word { background: #2563eb; }
    .badge.excel { background: #16a34a; }
    .badge.txt { background: #64748b; }
    .badge.image { background: #9333ea; }
    .thumb { flex: none; width: 34px; height: 34px; border-radius: 8px; object-fit: cover; background: var(--surface-hover); }
    .badge.other { background: var(--text-muted); }
    .text { display: flex; flex-direction: column; min-width: 0; flex: 1; }
    .name { font-size: 0.8125rem; font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .meta { font-size: 0.75rem; color: var(--text-muted); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .error-text { color: var(--danger); }
    .remove {
      flex: none; width: 24px; height: 24px; display: grid; place-items: center; border: 0; border-radius: 50%;
      background: var(--surface-hover); color: var(--text); cursor: pointer;
    }
    .remove:hover { background: color-mix(in srgb, var(--danger) 18%, var(--surface-hover)); }
    .download {
      flex: none; width: 28px; height: 28px; display: grid; place-items: center; border: 0; border-radius: 8px;
      background: transparent; color: var(--text-muted); cursor: pointer;
    }
    .download:hover:not(:disabled) { background: var(--surface-hover); color: var(--accent); }
    .download:disabled { cursor: progress; }
    .spinner {
      width: 14px; height: 14px; border-radius: 50%; border: 2px solid color-mix(in srgb, var(--accent) 25%, transparent);
      border-top-color: var(--accent); animation: spin 0.8s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }
    .progress { position: absolute; left: 0; right: 0; bottom: 0; height: 3px; background: var(--surface-hover); }
    .bar { display: block; height: 100%; background: var(--brand-gradient); transition: width 120ms linear; }
    .indeterminate .bar { width: 35%; animation: slide 1.1s ease-in-out infinite; }
    @keyframes slide { from { transform: translateX(-100%); } to { transform: translateX(300%); } }
    @media (prefers-reduced-motion: reduce) {
      .indeterminate .bar { animation: none; width: 100%; opacity: 0.5; }
      .spinner { animation-duration: 2.4s; }
    }
  `,
})
export class AttachmentChip {
  readonly fileName = input.required<string>();
  readonly fileSize = input(0);
  readonly state = input<ChipState>('ready');
  readonly progress = input(0);
  readonly error = input<string | null>(null);
  /** While processing: the server's current step, e.g. "Reading scanned pages… 3/12". */
  readonly detail = input<string | null>(null);
  /** Images: shown in place of the type badge. */
  readonly thumbnailUrl = input<string | null>(null);
  readonly removable = input(false);
  /** Shows a download button for the original file (sent attachments). */
  readonly downloadable = input(false);
  readonly downloading = input(false);
  readonly remove = output<void>();
  readonly download = output<void>();

  protected readonly extension = computed(() => {
    const name = this.fileName().toLowerCase();
    const dot = name.lastIndexOf('.');
    return dot >= 0 ? name.slice(dot) : '';
  });

  protected readonly kind = computed(() => {
    switch (this.extension()) {
      case '.pdf':
        return 'pdf';
      case '.docx':
        return 'word';
      case '.xlsx':
      case '.xls':
        return 'excel';
      case '.txt':
        return 'txt';
      case '.png':
      case '.jpg':
      case '.jpeg':
      case '.webp':
        return 'image';
      default:
        return 'other';
    }
  });

  protected readonly badge = computed(() => {
    switch (this.kind()) {
      case 'pdf':
        return 'PDF';
      case 'word':
        return 'DOC';
      case 'excel':
        return 'XLS';
      case 'txt':
        return 'TXT';
      case 'image':
        return 'IMG';
      default:
        return 'FILE';
    }
  });

  protected readonly meta = computed(() => {
    switch (this.state()) {
      case 'uploading':
        return `Uploading… ${this.progress()}%`;
      case 'processing':
        return this.detail() ?? 'Reading document…';
      case 'error':
        return this.error() ?? 'Upload failed';
      default:
        return formatFileSize(this.fileSize());
    }
  });
}
