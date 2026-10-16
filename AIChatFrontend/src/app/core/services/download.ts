import { HttpResponse } from '@angular/common/http';

/**
 * Browser downloads without opening a new page: a temporary object URL on a hidden <a download> link.
 */
export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.style.display = 'none';
  document.body.appendChild(link);
  link.click();
  link.remove();
  // Give the browser a moment to start the download before the URL is released.
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/** Saves a file response from the API under the name the server chose (Content-Disposition), or the fallback. */
export function saveResponse(response: HttpResponse<Blob>, fallbackName: string): void {
  saveBlob(response.body ?? new Blob(), fileNameFromDisposition(response.headers.get('Content-Disposition'), fallbackName));
}

/** A title made safe for every OS: no \ / : * ? " < > | or control characters, collapsed spaces, at most 60 characters. */
export function safeFileName(title: string, fallback = 'Chat'): string {
  const cleaned = title
    .replace(/[\\/:*?"<>|\p{C}]/gu, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .replace(/^\.+|\.+$/g, '')
    .slice(0, 60)
    .trim();
  return cleaned || fallback;
}

/** File name from a Content-Disposition header (RFC 5987 filename* first, then filename). */
export function fileNameFromDisposition(header: string | null, fallback: string): string {
  if (!header) {
    return fallback;
  }
  const star = /filename\*\s*=\s*UTF-8''([^;]+)/i.exec(header);
  if (star) {
    try {
      return decodeURIComponent(star[1].trim());
    } catch {
      // fall through to the plain filename
    }
  }
  const plain = /filename\s*=\s*"?([^";]+)"?/i.exec(header);
  return plain ? plain[1].trim() : fallback;
}
