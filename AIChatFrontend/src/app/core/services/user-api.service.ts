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
}
