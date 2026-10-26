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

  /** Replaces both instructions; blank clears a field. */
  updateSettings(settings: UserSettings): Observable<UserSettings> {
    return this.http.put<UserSettings>(`${this.url}/settings`, settings);
  }
}

/** GET/PUT /api/users/me/settings: custom instructions added to every chat (null = not set). */
export interface UserSettings {
  aboutMe: string | null;
  responseStyle: string | null;
}

/** Same limit as the API (UserSettings.MaxInstructionLength). */
export const MAX_INSTRUCTION_LENGTH = 1500;
