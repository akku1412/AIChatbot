import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { environment } from '../../../environments/environment';
import { errorMessage } from '../http/api-error';

export type RecorderState = 'idle' | 'starting' | 'recording' | 'transcribing';

/** Recordings stop by themselves after this long (the API accepts up to 10 MB, far more than this). */
export const MAX_RECORDING_SECONDS = 120;

/** Formats MediaRecorder may produce, best first (Chrome/Edge: WebM, Firefox: Ogg/WebM, Safari: MP4). */
const PREFERRED_TYPES = ['audio/webm;codecs=opus', 'audio/webm', 'audio/ogg;codecs=opus', 'audio/mp4'];

/** A recording/transcription problem with a message meant for the user. */
export class VoiceInputError extends Error {}

/**
 * The composer's microphone: records with MediaRecorder, then sends the audio to POST /api/audio/transcribe and
 * returns the text. One recording at a time; the microphone is released as soon as recording ends.
 */
@Injectable({ providedIn: 'root' })
export class VoiceRecorderService {
  private readonly http = inject(HttpClient);

  readonly state = signal<RecorderState>('idle');
  /** Seconds recorded so far. */
  readonly seconds = signal(0);

  private recorder: MediaRecorder | null = null;
  private stream: MediaStream | null = null;
  private chunks: Blob[] = [];
  private timer: ReturnType<typeof setInterval> | null = null;
  private finished: ((blob: Blob | null) => void) | null = null;

  /** Recording needs a secure page (https or localhost), getUserMedia and MediaRecorder. */
  get supported(): boolean {
    return (
      typeof window !== 'undefined' &&
      window.isSecureContext &&
      typeof MediaRecorder !== 'undefined' &&
      !!navigator.mediaDevices?.getUserMedia
    );
  }

  /** Asks for the microphone and starts recording. Throws VoiceInputError with a user-facing reason. */
  async start(): Promise<void> {
    if (this.state() !== 'idle') {
      return;
    }
    if (!this.supported) {
      throw new VoiceInputError("This browser can't record audio here.");
    }
    this.state.set('starting');
    try {
      this.stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    } catch (err) {
      this.state.set('idle');
      throw new VoiceInputError(permissionMessage(err));
    }

    const mimeType = PREFERRED_TYPES.find((type) => MediaRecorder.isTypeSupported(type));
    this.chunks = [];
    this.recorder = new MediaRecorder(this.stream, mimeType ? { mimeType } : undefined);
    this.recorder.ondataavailable = (event) => event.data.size > 0 && this.chunks.push(event.data);
    this.recorder.onstop = () => {
      const blob = this.chunks.length ? new Blob(this.chunks, { type: this.recorder?.mimeType || mimeType || 'audio/webm' }) : null;
      this.release();
      this.finished?.(blob);
      this.finished = null;
    };
    this.recorder.start();
    this.seconds.set(0);
    this.state.set('recording');
    // The composer finishes the recording when this reaches MAX_RECORDING_SECONDS.
    this.timer = setInterval(() => this.seconds.update((s) => s + 1), 1000);
  }

  /** Stops recording and returns the transcript. Throws VoiceInputError (nothing heard, server error…). */
  async stopAndTranscribe(): Promise<string> {
    const blob = await this.stopRecording();
    if (!blob) {
      this.state.set('idle');
      throw new VoiceInputError('Nothing was recorded. Try again.');
    }
    this.state.set('transcribing');
    try {
      const form = new FormData();
      form.append('audio', blob, `recording.${extensionFor(blob.type)}`);
      const result = await firstValueFrom(this.http.post<{ text: string }>(`${environment.apiBaseUrl}/audio/transcribe`, form));
      return result.text;
    } catch (err) {
      throw new VoiceInputError(errorMessage(err));
    } finally {
      this.state.set('idle');
    }
  }

  /** Stops recording and throws the audio away (Esc, or leaving the page). */
  cancel(): void {
    this.finished = null;
    if (this.recorder?.state === 'recording') {
      this.recorder.stop();
    } else {
      this.release();
    }
    this.state.set('idle');
  }

  private stopRecording(): Promise<Blob | null> {
    return new Promise((resolve) => {
      if (this.recorder?.state === 'recording') {
        this.finished = resolve;
        this.recorder.stop();
      } else {
        this.release();
        resolve(null);
      }
    });
  }

  private release(): void {
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = null;
    }
    this.stream?.getTracks().forEach((track) => track.stop()); // turns the browser's "recording" indicator off
    this.stream = null;
    this.recorder = null;
  }
}

function permissionMessage(err: unknown): string {
  const name = (err as { name?: string } | null)?.name;
  switch (name) {
    case 'NotAllowedError':
    case 'SecurityError':
      return 'Microphone access is blocked. Allow it for this site in your browser settings, then try again.';
    case 'NotFoundError':
    case 'OverconstrainedError':
      return 'No microphone was found. Connect one and try again.';
    case 'NotReadableError':
      return 'The microphone is in use by another app. Close it and try again.';
    default:
      return "Couldn't start the microphone.";
  }
}

function extensionFor(type: string): string {
  return type.includes('ogg') ? 'ogg' : type.includes('mp4') ? 'm4a' : 'webm';
}
