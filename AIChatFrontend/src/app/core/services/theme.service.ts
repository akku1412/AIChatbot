import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';

import { UserApiService } from './user-api.service';

export type ThemePreference = 'system' | 'light' | 'dark';
export type Theme = 'light' | 'dark';

/** Same key as public/theme-init.js, which applies the saved choice before the app starts. */
export const THEME_STORAGE_KEY = 'aichat.theme';

/**
 * Light / dark / follow the device. The choice is applied as <html data-theme>, kept in localStorage (so the next
 * load starts in the right theme) and saved in the user's settings on the server (so it follows them to other devices).
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly users = inject(UserApiService);
  private readonly media = typeof window !== 'undefined' && window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;
  private readonly deviceDark = signal(this.media?.matches ?? false);

  readonly preference = signal<ThemePreference>(readStored());
  /** What is on screen now. */
  readonly theme = computed<Theme>(() => {
    const preference = this.preference();
    return preference === 'system' ? (this.deviceDark() ? 'dark' : 'light') : preference;
  });

  constructor() {
    const onChange = (event: MediaQueryListEvent) => this.deviceDark.set(event.matches);
    this.media?.addEventListener('change', onChange);
    inject(DestroyRef).onDestroy(() => this.media?.removeEventListener('change', onChange));
    apply(this.preference());
  }

  /** Changes the theme now and saves it (locally at once, then to the account). */
  set(preference: ThemePreference): void {
    this.preference.set(preference);
    apply(preference);
    store(preference);
    this.users.updateSettings({ theme: preference }).subscribe({ error: () => undefined }); // the local choice still applies
  }

  /** After sign-in: the account's choice wins over this device's (e.g. set on another computer). */
  loadFromAccount(): void {
    this.users.getSettings().subscribe({
      next: ({ theme }) => {
        if (theme && theme !== this.preference()) {
          this.preference.set(theme);
          apply(theme);
          store(theme);
        }
      },
      error: () => undefined,
    });
  }

  /** Light → dark → light (the quick toggle); "system" becomes the opposite of what is showing. */
  toggle(): void {
    this.set(this.theme() === 'dark' ? 'light' : 'dark');
  }
}

function apply(preference: ThemePreference): void {
  const root = document.documentElement;
  if (preference === 'system') {
    root.removeAttribute('data-theme');
  } else {
    root.setAttribute('data-theme', preference);
  }
}

function readStored(): ThemePreference {
  try {
    const value = localStorage.getItem(THEME_STORAGE_KEY);
    return value === 'light' || value === 'dark' ? value : 'system';
  } catch {
    return 'system';
  }
}

function store(preference: ThemePreference): void {
  try {
    if (preference === 'system') {
      localStorage.removeItem(THEME_STORAGE_KEY);
    } else {
      localStorage.setItem(THEME_STORAGE_KEY, preference);
    }
  } catch {
    // Storage blocked: the theme still applies for this visit and is saved on the account.
  }
}
