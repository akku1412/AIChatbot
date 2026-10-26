import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';

import { environment } from '../../../environments/environment';
import { AiModel, AiModels } from '../models/chat.models';

/** The models users may pick per chat (GET /api/models), loaded once. */
@Injectable({ providedIn: 'root' })
export class ModelService {
  private readonly http = inject(HttpClient);
  private loaded = false;

  /** Empty until loaded (the picker stays hidden; chats use the server's default). */
  readonly models = signal<AiModel[]>([]);
  readonly defaultModelId = signal<string | null>(null);
  /** The server has a search API key: the composer offers "Search the web". */
  readonly webSearchAvailable = signal(false);
  /** The server has speech-to-text: the composer offers the microphone. */
  readonly voiceInputAvailable = signal(false);

  load(): void {
    if (this.loaded) {
      return;
    }
    this.loaded = true;
    this.http.get<AiModels>(`${environment.apiBaseUrl}/models`).subscribe({
      next: ({ models, defaultModelId, webSearchAvailable, voiceInputAvailable }) => {
        this.models.set(models);
        this.defaultModelId.set(defaultModelId);
        this.webSearchAvailable.set(webSearchAvailable ?? false);
        this.voiceInputAvailable.set(voiceInputAvailable ?? false);
      },
      error: () => (this.loaded = false), // retry next time; chats still work with the default model
    });
  }

  /** The id itself if it is offered, else the default (e.g. a model that was removed from the server config). */
  effectiveId(modelId: string | null | undefined): string | null {
    return modelId && this.models().some((m) => m.id === modelId) ? modelId : this.defaultModelId();
  }
}
