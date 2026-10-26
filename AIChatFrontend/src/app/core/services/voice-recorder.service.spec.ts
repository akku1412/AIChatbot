import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { VoiceInputError, VoiceRecorderService } from './voice-recorder.service';

/** A minimal MediaRecorder: start() records, stop() delivers one chunk then "stop". */
class FakeMediaRecorder {
  static supported = ['audio/webm;codecs=opus', 'audio/webm'];
  static isTypeSupported = (type: string) => FakeMediaRecorder.supported.includes(type);
  state: 'inactive' | 'recording' = 'inactive';
  mimeType: string;
  ondataavailable: ((event: { data: Blob }) => void) | null = null;
  onstop: (() => void) | null = null;

  constructor(_stream: unknown, options?: { mimeType?: string }) {
    this.mimeType = options?.mimeType ?? '';
  }

  start(): void {
    this.state = 'recording';
  }

  stop(): void {
    this.state = 'inactive';
    this.ondataavailable?.({ data: new Blob([new Uint8Array([0x1a, 0x45, 0xdf, 0xa3])], { type: this.mimeType }) });
    this.onstop?.();
  }
}

describe('VoiceRecorderService', () => {
  let service: VoiceRecorderService;
  let http: HttpTestingController;
  let track: { stop: ReturnType<typeof vi.fn> };
  let getUserMedia: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.useFakeTimers();
    track = { stop: vi.fn() };
    getUserMedia = vi.fn().mockResolvedValue({ getTracks: () => [track] });
    vi.stubGlobal('MediaRecorder', FakeMediaRecorder);
    Object.defineProperty(navigator, 'mediaDevices', { value: { getUserMedia }, configurable: true });
    Object.defineProperty(window, 'isSecureContext', { value: true, configurable: true });

    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(VoiceRecorderService);
  });

  afterEach(() => {
    // Restore globals first, so a failed test can't leak fake timers or stubs into other spec files.
    vi.unstubAllGlobals();
    vi.useRealTimers();
    http.verify();
  });

  /** Lets the recorder's promise chain (stop → blob → upload) run. */
  const settle = async () => {
    for (let i = 0; i < 5; i++) {
      await Promise.resolve();
    }
  };

  it('records, counts seconds, uploads the audio and returns the transcript', async () => {
    await service.start();
    expect(getUserMedia).toHaveBeenCalledWith({ audio: true });
    expect(service.state()).toBe('recording');
    vi.advanceTimersByTime(3000);
    expect(service.seconds()).toBe(3);

    const result = service.stopAndTranscribe();
    await settle();
    expect(track.stop).toHaveBeenCalled(); // the microphone is released at once
    expect(service.state()).toBe('transcribing');

    const request = http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/audio/transcribe'));
    const audio = (request.request.body as FormData).get('audio') as File;
    expect(audio.name).toBe('recording.webm');
    expect(audio.type).toBe('audio/webm;codecs=opus');
    request.flush({ text: 'Book a table for two' });

    expect(await result).toBe('Book a table for two');
    expect(service.state()).toBe('idle');
  });

  it('explains a blocked microphone', async () => {
    getUserMedia.mockRejectedValue(Object.assign(new Error('denied'), { name: 'NotAllowedError' }));

    await expect(service.start()).rejects.toThrow(/Microphone access is blocked/);
    expect(service.state()).toBe('idle');
  });

  it('explains a missing microphone', async () => {
    getUserMedia.mockRejectedValue(Object.assign(new Error('none'), { name: 'NotFoundError' }));
    await expect(service.start()).rejects.toThrow(/No microphone was found/);
  });

  it("passes on the server's reason when transcription fails", async () => {
    await service.start();
    const result = service.stopAndTranscribe();
    await settle();
    http.expectOne((r) => r.url.endsWith('/audio/transcribe')).flush(
      { title: 'No speech recognized', detail: "We couldn't hear any words in that recording." },
      { status: 422, statusText: 'Unprocessable' },
    );
    await expect(result).rejects.toBeInstanceOf(VoiceInputError);
    await expect(result).rejects.toThrow(/couldn't hear any words/);
    expect(service.state()).toBe('idle');
  });

  it('cancel discards the recording and frees the microphone without uploading', async () => {
    await service.start();
    service.cancel();
    expect(service.state()).toBe('idle');
    expect(track.stop).toHaveBeenCalled();
    http.expectNone((r) => r.url.endsWith('/audio/transcribe'));
  });

  it('is unavailable on insecure pages', () => {
    Object.defineProperty(window, 'isSecureContext', { value: false, configurable: true });
    expect(service.supported).toBe(false);
  });
});
