import { Injectable } from '@angular/core';

import { environment } from '../../../environments/environment';

/** Minimal typings for the parts of Google Identity Services (GIS) we use. */
interface CredentialResponse {
  credential: string;
}

interface GoogleAccountsId {
  initialize(config: {
    client_id: string;
    callback: (response: CredentialResponse) => void;
    auto_select?: boolean;
    cancel_on_tap_outside?: boolean;
    ux_mode?: 'popup' | 'redirect';
    itp_support?: boolean;
  }): void;
  renderButton(
    parent: HTMLElement,
    options: {
      type?: 'standard' | 'icon';
      theme?: 'outline' | 'filled_blue' | 'filled_black';
      size?: 'large' | 'medium' | 'small';
      text?: 'signin_with' | 'signup_with' | 'continue_with' | 'signin';
      shape?: 'rectangular' | 'pill' | 'circle' | 'square';
      logo_alignment?: 'left' | 'center';
      /** Pixels as a string, 200-400 (GIS ignores numbers in some browsers). */
      width?: string;
    },
  ): void;
  disableAutoSelect(): void;
}

declare global {
  interface Window {
    google?: { accounts: { id: GoogleAccountsId } };
  }
}

const GIS_SRC = 'https://accounts.google.com/gsi/client';

/**
 * Loads Google's official sign-in button. The browser only receives a signed ID token ("credential");
 * the API verifies it. No client secret or OAuth code ever touches the frontend.
 */
@Injectable({ providedIn: 'root' })
export class GoogleIdentityService {
  private loading: Promise<GoogleAccountsId> | null = null;

  async renderButton(
    host: HTMLElement,
    onCredential: (idToken: string) => void,
    options: { theme: 'outline' | 'filled_black'; width: number },
  ): Promise<void> {
    const gis = await this.load();
    gis.initialize({
      client_id: environment.googleClientId,
      callback: (response) => onCredential(response.credential),
      auto_select: false,
      cancel_on_tap_outside: true,
      ux_mode: 'popup',
      itp_support: true,
    });
    gis.renderButton(host, {
      type: 'standard',
      theme: options.theme,
      size: 'large',
      text: 'continue_with',
      shape: 'pill',
      logo_alignment: 'left',
      width: String(Math.round(options.width)),
    });
  }

  /** Stops Google from silently re-selecting the account after logout. */
  disableAutoSelect(): void {
    window.google?.accounts.id.disableAutoSelect();
  }

  private load(): Promise<GoogleAccountsId> {
    if (window.google?.accounts?.id) {
      return Promise.resolve(window.google.accounts.id);
    }

    this.loading ??= new Promise<GoogleAccountsId>((resolve, reject) => {
      const script = document.createElement('script');
      script.src = GIS_SRC;
      script.async = true;
      script.defer = true;
      script.onload = () =>
        window.google?.accounts?.id
          ? resolve(window.google.accounts.id)
          : reject(new Error('Google sign-in did not initialize.'));
      script.onerror = () => {
        this.loading = null; // allow a retry
        script.remove();
        reject(new Error('Could not load Google sign-in. Check your connection or disable blockers for this site.'));
      };
      document.head.appendChild(script);
    });
    return this.loading;
  }
}
