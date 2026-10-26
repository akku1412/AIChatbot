import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ComposerSubmit, MessageInput } from './message-input';

describe('MessageInput', () => {
  let fixture: ComponentFixture<MessageInput>;
  let http: HttpTestingController;
  let textarea: HTMLTextAreaElement;
  let sent: ComposerSubmit[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MessageInput],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MessageInput);
    sent = [];
    fixture.componentInstance.send.subscribe((payload) => sent.push(payload));
    await fixture.whenStable();
    http.match((req) => req.url.endsWith('/files/limits')); // keep the defaults
    textarea = fixture.nativeElement.querySelector('textarea');
  });

  function type(value: string): void {
    textarea.value = value;
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function press(init: KeyboardEventInit): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true, ...init });
    textarea.dispatchEvent(event);
    return event;
  }

  function pick(...files: File[]): void {
    const picker: HTMLInputElement = fixture.nativeElement.querySelector('input[type=file]');
    // jsdom has no DataTransfer, so hand the input a plain FileList-like array.
    Object.defineProperty(picker, 'files', { value: files, configurable: true });
    picker.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  const pdf = (name = 'report.pdf') => new File(['%PDF-1.4 test'], name, { type: 'application/pdf' });

  it('sends trimmed text on Enter', () => {
    type('  hello  ');
    const event = press({});
    expect(event.defaultPrevented).toBe(true);
    expect(sent).toEqual([{ text: 'hello', attachments: [] }]);
  });

  it('does not send on Shift+Enter (new line instead)', () => {
    type('line one');
    const event = press({ shiftKey: true });
    expect(event.defaultPrevented).toBe(false);
    expect(sent).toEqual([]);
  });

  it('does not send while an IME composition is active', () => {
    type('namaste');
    press({ isComposing: true });
    expect(sent).toEqual([]);
  });

  it('does not send blank text or while busy', () => {
    type('   ');
    press({});
    fixture.componentRef.setInput('busy', true);
    type('hi');
    press({});
    expect(sent).toEqual([]);
  });

  it('while busy, the button stops generation but Enter does not', () => {
    const stops: number[] = [];
    fixture.componentInstance.stop.subscribe(() => stops.push(1));
    fixture.componentRef.setInput('busy', true);
    type('next question');
    fixture.detectChanges();

    press({});
    expect(stops).toEqual([]);
    expect(sent).toEqual([]);

    const button: HTMLButtonElement = fixture.nativeElement.querySelector('button.send');
    expect(button.getAttribute('aria-label')).toBe('Stop generating');
    button.click();
    expect(stops).toEqual([1]);

    fixture.componentRef.setInput('stopping', true);
    fixture.detectChanges();
    expect(button.disabled).toBe(true);
  });

  it('rejects unsupported files without uploading them', () => {
    pick(new File(['x'], 'notes.rtf', { type: 'application/rtf' }));
    http.expectNone((req) => req.url.endsWith('/files/upload'));
    expect(fixture.nativeElement.querySelector('app-attachment-chip')).toBeNull();
  });

  it('accepts .txt files', () => {
    pick(new File(['meeting notes'], 'notes.txt', { type: 'text/plain' }));
    http.expectOne((req) => req.url.endsWith('/files/upload'));
    expect(fixture.nativeElement.querySelector('app-attachment-chip .badge')?.textContent).toBe('TXT');
  });

  it('uploads a file, blocks sending until it is ready, then sends its id', () => {
    pick(pdf());
    const upload = http.expectOne((req) => req.url.endsWith('/files/upload'));
    expect(fixture.nativeElement.querySelector('app-attachment-chip')).not.toBeNull();

    type('summarize');
    press({});
    expect(sent).toEqual([]); // still uploading

    upload.flush({
      fileId: 'f1', fileName: 'report.pdf', fileExtension: '.pdf', fileSize: 13,
      status: 'completed', statusMessage: null, pageCount: 1, sheetCount: null, chunkCount: 1, uploadedAt: '',
    });
    fixture.detectChanges();
    press({});
    expect(sent).toEqual([
      { text: 'summarize', attachments: [{ fileId: 'f1', fileName: 'report.pdf', fileExtension: '.pdf', fileSize: 13 }] },
    ]);
  });

  it('allows sending files without any text', () => {
    pick(pdf());
    http.expectOne((req) => req.url.endsWith('/files/upload')).flush({
      fileId: 'f2', fileName: 'report.pdf', fileExtension: '.pdf', fileSize: 13,
      status: 'completed', statusMessage: null, pageCount: 1, sheetCount: null, chunkCount: 1, uploadedAt: '',
    });
    fixture.detectChanges();
    press({});
    expect(sent.length).toBe(1);
    expect(sent[0].text).toBe('');
  });

  it('removing an uploaded file deletes it on the server', () => {
    pick(pdf());
    http.expectOne((req) => req.url.endsWith('/files/upload')).flush({
      fileId: 'f3', fileName: 'report.pdf', fileExtension: '.pdf', fileSize: 13,
      status: 'completed', statusMessage: null, pageCount: 1, sheetCount: null, chunkCount: 1, uploadedAt: '',
    });
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('app-attachment-chip .remove') as HTMLButtonElement).click();
    fixture.detectChanges();
    const del = http.expectOne((req) => req.method === 'DELETE' && req.url.endsWith('/files/f3'));
    del.flush(null);
    expect(fixture.nativeElement.querySelector('app-attachment-chip')).toBeNull();
  });

  describe('background processing (OCR)', () => {
    const file = (status: string, extra: object = {}) => ({
      fileId: 'f9', fileName: 'scan.pdf', fileExtension: '.pdf', contentType: 'application/pdf', fileSize: 13,
      status, statusMessage: null, progressCurrent: null, progressTotal: null, pageCount: null,
      extractedCharacters: 0, isImage: false, createdAt: '', ...extra,
    });
    const meta = () => (fixture.nativeElement.querySelector('app-attachment-chip .meta') as HTMLElement).textContent?.trim();
    const poll = () => http.expectOne((req) => req.method === 'GET' && req.url.endsWith('/files/f9'));

    beforeEach(() => vi.useFakeTimers());
    afterEach(() => vi.useRealTimers());

    function uploadProcessing(): void {
      pick(pdf('scan.pdf'));
      http.expectOne((req) => req.url.endsWith('/files/upload')).flush(file('processing'));
      fixture.detectChanges();
    }

    it('polls until the file is read, showing scanned-page progress, then allows sending', () => {
      uploadProcessing();
      type('what does it say?');
      expect(meta()).toBe('Reading document…');

      vi.advanceTimersByTime(1000);
      poll().flush(file('processing', { statusMessage: 'Reading scanned pages', progressCurrent: 3, progressTotal: 12 }));
      fixture.detectChanges();
      expect(meta()).toBe('Reading scanned pages… 3/12');
      expect(fixture.nativeElement.querySelector('app-attachment-chip [role=progressbar]').getAttribute('aria-valuenow')).toBe('25');
      press({});
      expect(sent).toEqual([]); // not ready yet

      vi.advanceTimersByTime(1000);
      poll().flush(file('completed', { pageCount: 12 }));
      fixture.detectChanges();
      press({});
      expect(sent.length).toBe(1);
      expect(sent[0].attachments[0].fileId).toBe('f9');

      vi.advanceTimersByTime(5000);
      http.expectNone((req) => req.url.endsWith('/files/f9')); // polling stopped
    });

    it('shows the server reason when processing fails', () => {
      uploadProcessing();
      vi.advanceTimersByTime(1000);
      poll().flush(file('failed', { statusMessage: 'This file took too long to process.', statusReason: 'timeout' }));
      fixture.detectChanges();
      expect(meta()).toBe('This file took too long to process.');
      expect(fixture.nativeElement.querySelector('.hint .over')).not.toBeNull();
    });

    it('removing a file while it is processing stops polling and deletes it', () => {
      uploadProcessing();
      (fixture.nativeElement.querySelector('app-attachment-chip .remove') as HTMLButtonElement).click();
      fixture.detectChanges();
      http.expectOne((req) => req.method === 'DELETE' && req.url.endsWith('/files/f9')).flush(null);
      vi.advanceTimersByTime(5000);
      http.expectNone((req) => req.method === 'GET' && req.url.endsWith('/files/f9'));
    });
  });

  it('removing a file mid-upload cancels the request', () => {
    pick(pdf());
    const upload = http.expectOne((req) => req.url.endsWith('/files/upload'));
    (fixture.nativeElement.querySelector('app-attachment-chip .remove') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(upload.cancelled).toBe(true);
  });
});
