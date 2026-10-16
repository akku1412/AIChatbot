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

/** Plain text as a UTF-8 .txt file. CRLF line breaks and a BOM keep it readable in every editor, including Notepad. */
export function saveTextFile(text: string, fileName: string): void {
  const normalized = text.replace(/\r\n?/g, '\n').replace(/\n/g, '\r\n');
  saveBlob(new Blob(['﻿', normalized], { type: 'text/plain;charset=utf-8' }), fileName);
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

/** 2026-10-16 1123 (local time) for file names. */
export function fileTimestamp(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}${pad(date.getMinutes())}`;
}
