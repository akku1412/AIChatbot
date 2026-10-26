/**
 * The browser's IANA time zone (e.g. "Asia/Kolkata"), sent to the API where it writes times into files (exports).
 * Times on screen need nothing: the API returns UTC ("…Z") and Angular's date pipe shows them in local time.
 * "UTC" when the browser can't tell, which is also the API's default.
 */
export function browserTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  } catch {
    return 'UTC';
  }
}
