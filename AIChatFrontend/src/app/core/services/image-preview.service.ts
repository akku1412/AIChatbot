import { Injectable, Signal, WritableSignal, inject, signal } from '@angular/core';

import { FileApiService } from './file-api.service';

/** Blob URLs need URL.createObjectURL (missing in some test environments). */
export function createObjectUrl(blob: Blob): string | null {
  return typeof URL.createObjectURL === 'function' ? URL.createObjectURL(blob) : null;
}

export function revokeObjectUrl(url: string | null): void {
  if (url && typeof URL.revokeObjectURL === 'function') {
    URL.revokeObjectURL(url);
  }
}

/**
 * Thumbnails of the user's uploaded images. Files are private (the download needs the auth header), so an
 * <img> can't point at the API: each image is fetched once and shown from a blob URL, cached by file id.
 * Images just attached in the composer are handed over with adopt(), so sending never re-downloads them.
 */
@Injectable({ providedIn: 'root' })
export class ImagePreviewService {
  private static readonly maxEntries = 60;

  private readonly fileApi = inject(FileApiService);
  private readonly urls = new Map<string, WritableSignal<string | null>>();

  /** Blob URL of the image; null while loading or when it can't be loaded (deleted, offline). */
  url(fileId: string): Signal<string | null> {
    let entry = this.urls.get(fileId);
    if (!entry) {
      const created = signal<string | null>(null);
      entry = created;
      this.store(fileId, created);
      this.fileApi.download(fileId).subscribe({
        next: (response) => {
          // Skip if it was forgotten or evicted meanwhile: nobody would revoke the URL.
          if (response.body && this.urls.get(fileId) === created) {
            created.set(createObjectUrl(response.body));
          }
        },
        error: () => undefined, // stays null: the message shows a placeholder
      });
    }
    return entry.asReadonly();
  }

  /** Takes ownership of a blob URL made from the local file (composer), so sent messages show it at once. */
  adopt(fileId: string, url: string): void {
    this.forget(fileId);
    this.store(fileId, signal(url));
  }

  /** Drops and revokes the cached URL (e.g. the attachment was removed before sending). */
  forget(fileId: string): void {
    revokeObjectUrl(this.urls.get(fileId)?.() ?? null);
    this.urls.delete(fileId);
  }

  private store(fileId: string, entry: WritableSignal<string | null>): void {
    if (this.urls.size >= ImagePreviewService.maxEntries) {
      // Map keeps insertion order: drop the oldest.
      const oldest = this.urls.keys().next().value;
      if (oldest !== undefined) {
        this.forget(oldest);
      }
    }
    this.urls.set(fileId, entry);
  }
}
