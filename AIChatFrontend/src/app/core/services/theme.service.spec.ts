import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { THEME_STORAGE_KEY, ThemeService } from './theme.service';

describe('ThemeService', () => {
  let http: HttpTestingController;
  let deviceDark: boolean;
  let listeners: ((event: { matches: boolean }) => void)[];

  beforeEach(() => {
    deviceDark = false;
    listeners = [];
    localStorage.removeItem(THEME_STORAGE_KEY);
    document.documentElement.removeAttribute('data-theme');
    // jsdom has no matchMedia: provide one that reports the "device" setting and records change listeners.
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: () => ({
        matches: deviceDark,
        addEventListener: (_: string, fn: (event: { matches: boolean }) => void) => listeners.push(fn),
        removeEventListener: () => undefined,
      }),
    });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    delete (window as { matchMedia?: unknown }).matchMedia;
    localStorage.removeItem(THEME_STORAGE_KEY);
    document.documentElement.removeAttribute('data-theme');
    http.verify();
  });

  it('follows the device by default, live', () => {
    deviceDark = true;
    const theme = TestBed.inject(ThemeService);
    expect(theme.preference()).toBe('system');
    expect(theme.theme()).toBe('dark');
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);

    listeners.forEach((fn) => fn({ matches: false })); // the device switches to light
    expect(theme.theme()).toBe('light');
  });

  it('applies, remembers and saves a choice', () => {
    const theme = TestBed.inject(ThemeService);

    theme.set('dark');

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark');
    const put = http.expectOne((r) => r.method === 'PUT' && r.url.endsWith('/users/me/settings'));
    expect(put.request.body).toEqual({ theme: 'dark' }); // only the theme: the instructions are left alone
    put.flush({ aboutMe: null, responseStyle: null, theme: 'dark' });

    theme.set('system');
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBeNull();
    http.expectOne((r) => r.method === 'PUT').flush({ aboutMe: null, responseStyle: null, theme: 'system' });
  });

  it("starts from this device's saved choice, then takes the account's", () => {
    localStorage.setItem(THEME_STORAGE_KEY, 'light');
    const theme = TestBed.inject(ThemeService);
    expect(theme.preference()).toBe('light');

    theme.loadFromAccount();
    http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/users/me/settings'))
      .flush({ aboutMe: null, responseStyle: null, theme: 'dark' });

    expect(theme.theme()).toBe('dark');
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark');
  });

  it('the quick toggle flips what is showing', () => {
    deviceDark = true;
    const theme = TestBed.inject(ThemeService);
    theme.toggle(); // system (dark) → light
    expect(theme.theme()).toBe('light');
    http.expectOne((r) => r.method === 'PUT').flush({});
    theme.toggle();
    expect(theme.theme()).toBe('dark');
    http.expectOne((r) => r.method === 'PUT').flush({});
  });

  it('keeps the choice for this visit when saving to the account fails', () => {
    const theme = TestBed.inject(ThemeService);
    theme.set('dark');
    http.expectOne((r) => r.method === 'PUT').flush(null, { status: 500, statusText: 'Server Error' });
    expect(theme.theme()).toBe('dark');
  });
});
