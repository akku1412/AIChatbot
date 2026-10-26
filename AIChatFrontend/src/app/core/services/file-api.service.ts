import { HttpClient, HttpEvent, HttpResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, exhaustMap, retry, takeWhile, timer } from 'rxjs';

import { environment } from '../../../environments/environment';
import { readBlobErrors } from '../http/api-error';
import { ChunkPreview, DEFAULT_UPLOAD_LIMITS, UploadLimits, UploadedFile } from '../models/chat.models';

/** True while the server is still reading the file. */
export function isProcessing(file: UploadedFile): boolean {
  return file.status === 'uploaded' || file.status === 'processing';
}

/** "Reading scanned pages… 3/12", "Indexing for search…", or null when the server gave no step. */
export function processingLabel(file: UploadedFile): string | null {
  if (!file.statusMessage) {
    return null;
  }
  return file.progressTotal
    ? `${file.statusMessage}… ${file.progressCurrent ?? 0}/${file.progressTotal}`
    : `${file.statusMessage}…`;
}

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

  /**
   * The server reads files in the background (OCR can take a while), so an upload may answer "processing".
   * Polls GET /api/files/{id}, emitting each update, and completes with the "completed" or "failed" file.
   * Brief network errors are retried; unsubscribing stops polling.
   */
  watch(fileId: string, intervalMs = 1000): Observable<UploadedFile> {
    return timer(intervalMs, intervalMs).pipe(
      exhaustMap(() => this.get(fileId)),
      retry({ count: 3, delay: intervalMs }),
      takeWhile((file) => isProcessing(file), true),
    );
  }

  /** One cited passage (owner only; anything else is 404). */
  getChunk(fileId: string, chunkId: number): Observable<ChunkPreview> {
    return this.http.get<ChunkPreview>(`${this.url}/${fileId}/chunks/${chunkId}`);
  }

  /** The original uploaded file (owner only); the server names it in Content-Disposition. */
  download(fileId: string): Observable<HttpResponse<Blob>> {
    return this.http
      .get(`${this.url}/${fileId}/download`, { observe: 'response', responseType: 'blob' })
      .pipe(readBlobErrors());
  }

  delete(fileId: string): Observable<void> {
    return this.http.delete<void>(`${this.url}/${fileId}`);
  }
}
