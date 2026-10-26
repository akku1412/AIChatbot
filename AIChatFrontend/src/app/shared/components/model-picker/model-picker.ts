import { Component, ElementRef, computed, inject, signal } from '@angular/core';

import { errorMessage } from '../../../core/http/api-error';
import { ChatStore } from '../../../core/services/chat.store';
import { ModelService } from '../../../core/services/model.service';
import { NotificationService } from '../toast/notification.service';

/**
 * "Smart ▾" in the chat header: picks the model for the open chat (saved on the server) or for the next new chat.
 * Hidden when the server offers only one model. Disabled while a reply is being generated.
 */
@Component({
  selector: 'app-model-picker',
  host: {
    '(document:click)': 'onDocumentClick($event)',
    '(document:keydown.escape)': 'open.set(false)',
  },
  template: `
    @if (models.models().length > 1) {
      <button type="button" class="trigger" aria-haspopup="menu" [attr.aria-expanded]="open()"
              [disabled]="store.sending() || saving()" [attr.aria-label]="'Model: ' + (current()?.label ?? '') + '. Change model'"
              (click)="open.set(!open())">
        <span>{{ current()?.label }}</span>
        <svg viewBox="0 0 24 24" width="14" height="14" aria-hidden="true"><path d="M6 9l6 6 6-6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>
      </button>
      @if (open()) {
        <div class="menu" role="menu" aria-label="Choose a model">
          @for (model of models.models(); track model.id) {
            <button type="button" role="menuitemradio" [attr.aria-checked]="model.id === store.activeModelId()" (click)="choose(model.id)">
              <span class="text">
                <span class="label">{{ model.label }}</span>
                @if (model.description) {
                  <span class="hint">{{ model.description }}</span>
                }
              </span>
              @if (model.id === store.activeModelId()) {
                <svg class="check" viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M5 12l5 5L20 7" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/></svg>
              }
            </button>
          }
        </div>
      }
    }
  `,
  styles: `
    :host { position: relative; display: inline-flex; }
    .trigger {
      display: inline-flex; align-items: center; gap: 4px; height: 36px; padding: 0 10px; border: 0; border-radius: 8px;
      background: transparent; color: var(--text); font: inherit; font-weight: 600; cursor: pointer;
    }
    .trigger:hover:not(:disabled) { background: var(--surface-hover); }
    .trigger:disabled { opacity: 0.6; cursor: default; }
    .menu {
      position: absolute; top: calc(100% + 4px); left: 0; z-index: 50; min-width: 240px; max-width: calc(100vw - 24px);
      padding: 6px; border: 1px solid var(--border); border-radius: 12px; background: var(--surface-raised);
      box-shadow: 0 8px 24px rgb(0 0 0 / 0.12);
    }
    .menu button {
      width: 100%; display: flex; align-items: center; gap: 10px; padding: 8px 10px; border: 0; border-radius: 8px;
      background: transparent; color: var(--text); font: inherit; text-align: left; cursor: pointer;
    }
    .menu button:hover, .menu button:focus-visible { background: var(--surface-hover); }
    .text { display: flex; flex-direction: column; flex: 1; min-width: 0; }
    .label { font-weight: 600; font-size: 0.875rem; }
    .hint { font-size: 0.75rem; color: var(--text-muted); }
    .check { flex: none; color: var(--accent); }
  `,
})
export class ModelPicker {
  protected readonly store = inject(ChatStore);
  protected readonly models = inject(ModelService);
  private readonly notifications = inject(NotificationService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly open = signal(false);
  protected readonly saving = signal(false);
  protected readonly current = computed(() => this.models.models().find((m) => m.id === this.store.activeModelId()) ?? null);

  constructor() {
    this.models.load();
  }

  protected choose(modelId: string): void {
    this.open.set(false);
    if (modelId === this.store.activeModelId()) {
      return;
    }
    this.saving.set(true);
    this.store.selectModel(modelId).subscribe({
      next: () => this.saving.set(false),
      error: (err: unknown) => {
        this.saving.set(false);
        this.notifications.error(`Couldn't change the model. ${errorMessage(err)}`);
      },
    });
  }

  protected onDocumentClick(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) {
      this.open.set(false);
    }
  }
}
