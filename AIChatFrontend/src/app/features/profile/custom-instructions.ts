import { Component, computed, inject, signal } from '@angular/core';

import { errorMessage } from '../../core/http/api-error';
import { MAX_INSTRUCTION_LENGTH, UserApiService, UserSettings } from '../../core/services/user-api.service';
import { LoadingSpinner } from '../../shared/components/loading-spinner/loading-spinner';
import { NotificationService } from '../../shared/components/toast/notification.service';

/**
 * Profile card for custom instructions: what AIChat should know about the user and how it should respond.
 * Saved on the server and added to every chat as preferences (they can't override the app's own rules).
 */
@Component({
  selector: 'app-custom-instructions',
  imports: [LoadingSpinner],
  template: `
    <section class="card" aria-labelledby="instructions-title">
      <h3 id="instructions-title">Custom instructions</h3>
      <p class="muted intro">Tell AIChat about yourself and how you like answers. This applies to all your chats, new and existing.</p>

      @if (loadError(); as error) {
        <p class="error" role="alert">{{ error }} <button type="button" class="link" (click)="load()">Try again</button></p>
      } @else if (!loaded()) {
        <div class="loading"><app-loading-spinner [size]="18" label="Loading custom instructions" /></div>
      } @else {
        <form (submit)="$event.preventDefault(); save()" novalidate>
          <label class="field-label" for="about-me">What should AIChat know about you?</label>
          <textarea id="about-me" rows="4" [value]="aboutMe()" (input)="aboutMe.set($any($event.target).value)"
                    [attr.maxlength]="max" placeholder="e.g. I'm a .NET developer working on an Angular app; I live in Pune."
                    aria-describedby="about-me-count"></textarea>
          <p class="count" id="about-me-count" [class.near]="aboutMe().length > max - 100">{{ aboutMe().length }}/{{ max }}</p>

          <label class="field-label" for="response-style">How should AIChat respond?</label>
          <textarea id="response-style" rows="4" [value]="responseStyle()" (input)="responseStyle.set($any($event.target).value)"
                    [attr.maxlength]="max" placeholder="e.g. Short answers, code first, explain trade-offs in bullet points."
                    aria-describedby="response-style-count"></textarea>
          <p class="count" id="response-style-count" [class.near]="responseStyle().length > max - 100">{{ responseStyle().length }}/{{ max }}</p>

          <div class="actions">
            <button type="button" class="btn secondary" [disabled]="!changed() || saving()" (click)="reset()">Discard changes</button>
            <button type="submit" class="btn primary" [disabled]="!changed() || saving() || tooLong()">
              @if (saving()) {
                <app-loading-spinner [size]="14" label="Saving" />
              }
              Save
            </button>
          </div>
        </form>
      }
    </section>
  `,
  styles: `
    .intro { margin: 0 0 14px; }
    .field-label { display: block; margin: 0 0 6px; font-size: 0.875rem; font-weight: 600; }
    textarea {
      width: 100%; box-sizing: border-box; min-height: 88px; padding: 10px 12px; resize: vertical;
      border: 1px solid var(--border); border-radius: 10px; background: var(--surface); color: var(--text); font: inherit;
    }
    textarea:focus-visible { outline: 2px solid var(--accent); outline-offset: 1px; }
    .count { margin: 4px 0 14px; text-align: right; font-size: 0.75rem; color: var(--text-muted); }
    .count.near { color: var(--danger); }
    .actions { display: flex; justify-content: flex-end; gap: 8px; }
    .loading { padding: 8px 0; }
    .error { color: var(--danger); }
    .link { border: 0; background: none; padding: 0; color: var(--accent); font: inherit; cursor: pointer; text-decoration: underline; }
  `,
  styleUrl: './profile-card.css',
})
export class CustomInstructions {
  private readonly users = inject(UserApiService);
  private readonly notifications = inject(NotificationService);

  protected readonly max = MAX_INSTRUCTION_LENGTH;
  protected readonly aboutMe = signal('');
  protected readonly responseStyle = signal('');
  private readonly saved = signal<UserSettings>({ aboutMe: null, responseStyle: null, theme: 'system' });
  protected readonly loaded = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly saving = signal(false);

  protected readonly changed = computed(
    () => this.aboutMe() !== (this.saved().aboutMe ?? '') || this.responseStyle() !== (this.saved().responseStyle ?? ''),
  );
  protected readonly tooLong = computed(() => this.aboutMe().length > this.max || this.responseStyle().length > this.max);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loadError.set(null);
    this.users.getSettings().subscribe({
      next: (settings) => {
        this.apply(settings);
        this.loaded.set(true);
      },
      error: (err: unknown) => this.loadError.set(`Couldn't load your custom instructions. ${errorMessage(err)}`),
    });
  }

  protected reset(): void {
    this.apply(this.saved());
  }

  protected save(): void {
    if (!this.changed() || this.saving() || this.tooLong()) {
      return;
    }
    this.saving.set(true);
    this.users.updateSettings({ aboutMe: this.aboutMe(), responseStyle: this.responseStyle() }).subscribe({
      next: (settings) => {
        this.apply(settings);
        this.saving.set(false);
        this.notifications.success('Custom instructions saved. They apply from your next message.');
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.notifications.error(errorMessage(err));
      },
    });
  }

  private apply(settings: UserSettings): void {
    this.saved.set(settings);
    this.aboutMe.set(settings.aboutMe ?? '');
    this.responseStyle.set(settings.responseStyle ?? '');
  }
}
