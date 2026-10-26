import { Injectable, signal } from '@angular/core';

import { MessageSource } from '../models/chat.models';

/** Which cited passage the source panel shows (one panel for the whole chat). */
@Injectable({ providedIn: 'root' })
export class SourcePreviewService {
  private readonly current = signal<MessageSource | null>(null);
  readonly source = this.current.asReadonly();

  show(source: MessageSource): void {
    this.current.set(source);
  }

  close(): void {
    this.current.set(null);
  }
}
