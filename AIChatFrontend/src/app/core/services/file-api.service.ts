import { HttpClient, HttpEvent } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { DEFAULT_UPLOAD_LIMITS, UploadLimits, UploadedFile } from '../models/chat.models';

/** Typed calls for /api/files. Uploads report progress; unsubscribing cancels the request. */
@Injectable({ providedIn: 'root' })
export class FileApiService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiBaseUrl}/files`;
  private limitsLoaded = false;

  /** Server limits (loaded once); defaults until then. */
  readonly limits = signal<UploadLimits>(DEFAULT_UPLOAD_LIMITS);

  loadLimits(): void {
    if (this.limitsLoaded) {
      return;
    }
    this.limitsLoaded = true;
    this.http.get<UploadLimits>(`${this.url}/limits`).subscribe({
      next: (limits) => this.limits.set(limits),
      error: () => (this.limitsLoaded = false), // keep defaults, retry next time
    });
  }

  /** Emits upload progress events, then the processed file (HttpResponse) once the server has read it. */
  upload(file: File): Observable<HttpEvent<UploadedFile>> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<UploadedFile>(`${this.url}/upload`, form, { reportProgress: true, observe: 'events' });
  }

  get(fileId: string): Observable<UploadedFile> {
    return this.http.get<UploadedFile>(`${this.url}/${fileId}`);
  }

  delete(fileId: string): Observable<void> {
    return this.http.delete<void>(`${this.url}/${fileId}`);
  }
}
