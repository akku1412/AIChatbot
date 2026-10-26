import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { User } from '../auth/auth.models';

/** GET/PUT /api/users/me. The API identifies the user from the access token only. */
@Injectable({ providedIn: 'root' })
export class UserApiService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiBaseUrl}/users/me`;

  getMe(): Observable<User> {
    return this.http.get<User>(this.url);
  }

  updateMe(fullName: string): Observable<User> {
    return this.http.put<User>(this.url, { fullName });
  }

  getSettings(): Observable<UserSettings> {
    return this.http.get<UserSettings>(`${this.url}/settings`);
  }

  /** Saves the fields that are present (missing ones stay as they are); a blank instruction clears it. */
  updateSettings(settings: Partial<UserSettings>): Observable<UserSettings> {
    return this.http.put<UserSettings>(`${this.url}/settings`, settings);
  }
}

/** GET/PUT /api/users/me/settings: custom instructions added to every chat (null = not set). */
export interface UserSettings {
  aboutMe: string | null;
  responseStyle: string | null;
  /** "system" follows the device. */
  theme: 'system' | 'light' | 'dark';
}

/** Same limit as the API (UserSettings.MaxInstructionLength). */
export const MAX_INSTRUCTION_LENGTH = 1500;
