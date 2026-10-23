import { Component, input, output } from '@angular/core';

/** "‹ 2/3 ›": steps through the versions of a message (regenerated replies, edited questions). */
@Component({
  selector: 'app-version-switcher',
  template: `
    <span class="switcher" role="group" [attr.aria-label]="'Version ' + current() + ' of ' + count()">
      <button type="button" [disabled]="disabled() || current() <= 1" aria-label="Previous version" (click)="go(current() - 1)">
        <svg viewBox="0 0 24 24" width="14" height="14" aria-hidden="true"><path d="M15 6l-6 6 6 6" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/></svg>
      </button>
      <span class="label" aria-live="polite">{{ current() }}/{{ count() }}</span>
      <button type="button" [disabled]="disabled() || current() >= count()" aria-label="Next version" (click)="go(current() + 1)">
        <svg viewBox="0 0 24 24" width="14" height="14" aria-hidden="true"><path d="M9 6l6 6-6 6" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/></svg>
      </button>
    </span>
  `,
  styles: `
    .switcher { display: inline-flex; align-items: center; gap: 2px; color: var(--text-muted); font-size: 0.8125rem; }
    .label { min-width: 2.6em; text-align: center; font-variant-numeric: tabular-nums; }
    button {
      width: 26px; height: 26px; display: grid; place-items: center; border: 0; border-radius: 6px;
      background: transparent; color: inherit; cursor: pointer;
    }
    button:hover:not(:disabled) { background: var(--surface-hover); color: var(--text); }
    button:disabled { opacity: 0.35; cursor: default; }
  `,
})
export class VersionSwitcher {
  readonly current = input.required<number>();
  readonly count = input.required<number>();
  readonly disabled = input(false);
  /** Emits the 1-based version to show. */
  readonly select = output<number>();

  protected go(version: number): void {
    if (version >= 1 && version <= this.count() && version !== this.current()) {
      this.select.emit(version);
    }
  }
}
