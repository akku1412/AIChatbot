import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ChatMessage } from '../../../core/models/chat.models';
import { NotificationService } from '../toast/notification.service';
import { ChatMessageView } from './chat-message';

describe('ChatMessageView', () => {
  let fixture: ComponentFixture<ChatMessageView>;
  let http: HttpTestingController;
  let errors: string[];
  let downloads: string[];
  const restore: (() => void)[] = [];

  const versions = { parentMessageId: null, versionNumber: 1, versionCount: 1, isTruncated: false };
  const reply: ChatMessage = {
    messageId: 42, role: 'assistant', content: '## Totals\n\n| A | B |\n|---|---|\n| 1 | 2 |',
    createdAt: '2026-10-16T06:00:00Z', tokenUsage: 10, attachments: [], ...versions,
  };
  const question: ChatMessage = {
    messageId: 41, role: 'user', content: 'Summarize', createdAt: '2026-10-16T06:00:00Z', tokenUsage: null,
    attachments: [{ fileId: 'f-1', fileName: 'sales.xlsx', fileExtension: '.xlsx', fileSize: 7139 }], ...versions,
  };

  async function render(message: ChatMessage, inputs: Record<string, unknown> = {}): Promise<HTMLElement> {
    fixture = TestBed.createComponent(ChatMessageView);
    fixture.componentRef.setInput('message', message);
    fixture.componentRef.setInput('conversationTitle', 'Sales');
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    errors = [];
    downloads = [];
    vi.spyOn(TestBed.inject(NotificationService), 'error').mockImplementation((m: string) => errors.push(m));
    const create = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:test');
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      downloads.push(this.download);
    });
    restore.push(() => create.mockRestore(), () => revoke.mockRestore(), () => click.mockRestore());
  });

  afterEach(() => {
    restore.splice(0).forEach((undo) => undo());
    http.verify();
  });

  function openMenu(el: HTMLElement): HTMLButtonElement[] {
    (el.querySelector('button[aria-haspopup="menu"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    return Array.from(el.querySelectorAll<HTMLButtonElement>('[role="menuitem"]'));
  }

  it('offers TXT, PDF, DOCX and XLSX for AI replies', async () => {
    const items = openMenu(await render(reply));
    expect(items.map((b) => b.querySelector('.label')?.textContent)).toEqual([
      'Download as TXT', 'Download as PDF', 'Download as DOCX', 'Download as XLSX',
    ]);
  });

  it('generates the file on the server, shows progress, then saves it under the server name', async () => {
    const el = await render(reply);
    openMenu(el)[1].click(); // PDF
    fixture.detectChanges();

    const req = http.expectOne((r) => r.url.endsWith('/messages/42/export'));
    expect(req.request.params.get('format')).toBe('pdf');
    expect(req.request.responseType).toBe('blob');
    expect(el.querySelector('button[aria-haspopup="menu"]')?.textContent).toContain('Preparing PDF…');
    expect(el.querySelector('[role="menu"]')).toBeNull();

    req.flush(new Blob(['%PDF-1.7']), {
      headers: { 'Content-Disposition': `attachment; filename*=UTF-8''AIChat%20reply%20-%20Sales.pdf` },
    });
    fixture.detectChanges();
    expect(downloads).toEqual(['AIChat reply - Sales.pdf']);
    expect(el.querySelector('button[aria-haspopup="menu"]')?.textContent).toContain('Download');
  });

  it("shows the server's reason when a format can't be generated", async () => {
    const el = await render(reply);
    openMenu(el)[3].click(); // XLSX
    const problem = { title: "Can't export in this format", detail: 'This reply has no table or list of values to put in a spreadsheet.' };
    http.expectOne((r) => r.url.endsWith('/messages/42/export'))
      .flush(new Blob([JSON.stringify(problem)], { type: 'application/problem+json' }), { status: 422, statusText: 'Unprocessable' });
    await vi.waitFor(() => expect(errors.length).toBe(1));
    expect(errors[0]).toBe("Couldn't create the XLSX file. This reply has no table or list of values to put in a spreadsheet.");
    expect(downloads).toEqual([]);
  });

  it('downloads the original attached file', async () => {
    const el = await render(question);
    expect(el.querySelector('[role="menu"], button[aria-haspopup="menu"]')).toBeNull(); // no export menu on user messages
    (el.querySelector('app-attachment-chip .download') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.querySelector('app-attachment-chip .download')?.getAttribute('aria-busy')).toBe('true');

    http.expectOne((r) => r.url.endsWith('/files/f-1/download')).flush(new Blob(['xlsx bytes']), {
      headers: { 'Content-Disposition': `attachment; filename*=UTF-8''sales.xlsx` },
    });
    fixture.detectChanges();
    expect(downloads).toEqual(['sales.xlsx']);
  });

  it('reports a failed original-file download', async () => {
    const el = await render(question);
    (el.querySelector('app-attachment-chip .download') as HTMLButtonElement).click();
    http.expectOne((r) => r.url.endsWith('/files/f-1/download')).flush(
      new Blob([JSON.stringify({ title: 'File not found', detail: 'This file is no longer available.' })]),
      { status: 404, statusText: 'Not Found' },
    );
    await vi.waitFor(() => expect(errors.length).toBe(1));
    expect(errors[0]).toBe('Couldn\'t download "sales.xlsx". This file is no longer available.');
  });

  it('edits a user message inline: Save & submit emits the new text, Cancel and unchanged text do not', async () => {
    const el = await render(question, { canEdit: true });
    const edits: string[] = [];
    fixture.componentInstance.edit.subscribe((text) => edits.push(text));

    (el.querySelector('button[aria-label="Edit message"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const box = el.querySelector('textarea') as HTMLTextAreaElement;
    expect(box.value).toBe('Summarize');
    const save = () => Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes('Save & submit')) as HTMLButtonElement;
    expect(save().disabled).toBe(true); // unchanged

    box.value = 'Summarize the totals';
    box.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    save().click();
    fixture.detectChanges();
    expect(edits).toEqual(['Summarize the totals']);
    expect(el.querySelector('textarea')).toBeNull();

    (el.querySelector('button[aria-label="Edit message"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    (Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Cancel') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(edits).toHaveLength(1);
  });

  it('has no edit button when editing is not allowed', async () => {
    const el = await render(question, { canEdit: false });
    expect(el.querySelector('button[aria-label="Edit message"]')).toBeNull();
  });

  it('shows "‹ 2/3 ›" for messages with versions and emits the chosen version', async () => {
    const el = await render({ ...reply, versionNumber: 2, versionCount: 3 });
    const chosen: number[] = [];
    fixture.componentInstance.switchVersion.subscribe((v) => chosen.push(v));

    expect(el.querySelector('app-version-switcher .label')?.textContent).toBe('2/3');
    (el.querySelector('button[aria-label="Previous version"]') as HTMLButtonElement).click();
    (el.querySelector('button[aria-label="Next version"]') as HTMLButtonElement).click();
    expect(chosen).toEqual([1, 3]);

    fixture.componentRef.setInput('message', { ...reply, versionNumber: 3, versionCount: 3 });
    fixture.detectChanges();
    expect((el.querySelector('button[aria-label="Next version"]') as HTMLButtonElement).disabled).toBe(true);

    fixture.componentRef.setInput('canSwitch', false); // e.g. while a reply streams
    fixture.detectChanges();
    expect((el.querySelector('button[aria-label="Previous version"]') as HTMLButtonElement).disabled).toBe(true);
  });

  describe('image attachments', () => {
    const withImage: ChatMessage = {
      ...question,
      attachments: [{ fileId: 'img-1', fileName: 'chart.png', fileExtension: '.png', fileSize: 2048, isImage: true }],
    };

    it('shows a thumbnail fetched once through the authenticated download, and opens it full size', async () => {
      const el = await render(withImage);
      expect(el.querySelector('app-attachment-chip')).toBeNull(); // images are thumbnails, not chips
      http.expectOne((r) => r.url.endsWith('/files/img-1/download')).flush(new Blob(['png bytes']));
      fixture.detectChanges();
      expect(el.querySelector('.image-file img')?.getAttribute('src')).toBe('blob:test');

      (el.querySelector('.image-file') as HTMLButtonElement).click();
      fixture.detectChanges();
      const viewer = el.querySelector('[role="dialog"]') as HTMLElement;
      expect(viewer.getAttribute('aria-label')).toBe('chart.png');
      expect(viewer.querySelector('img')?.getAttribute('src')).toBe('blob:test');

      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
      fixture.detectChanges();
      expect(el.querySelector('[role="dialog"]')).toBeNull();

      await render(withImage); // the same image elsewhere reuses the cached copy
      http.expectNone((r) => r.url.endsWith('/files/img-1/download'));
    });

    it('keeps a placeholder when the image cannot be loaded', async () => {
      const el = await render({ ...withImage, attachments: [{ ...withImage.attachments[0], fileId: 'img-gone' }] });
      http.expectOne((r) => r.url.endsWith('/files/img-gone/download')).flush(new Blob(['']), { status: 404, statusText: 'Not Found' });
      fixture.detectChanges();
      expect(el.querySelector('.image-file img')).toBeNull();
      expect(el.querySelector('.image-loading')).not.toBeNull();
      expect(errors).toEqual([]); // a missing thumbnail is not worth a toast
    });
  });

  it('marks a stopped reply with "(stopped)"', async () => {
    expect((await render({ ...reply, isTruncated: true })).querySelector('.stopped')?.textContent).toBe('(stopped)');
    expect((await render(reply)).querySelector('.stopped')).toBeNull();
  });
});
