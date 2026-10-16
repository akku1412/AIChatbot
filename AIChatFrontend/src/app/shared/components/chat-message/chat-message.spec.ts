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

  const reply: ChatMessage = {
    messageId: 42, role: 'assistant', content: '## Totals\n\n| A | B |\n|---|---|\n| 1 | 2 |',
    createdAt: '2026-10-16T06:00:00Z', tokenUsage: 10, attachments: [],
  };
  const question: ChatMessage = {
    messageId: 41, role: 'user', content: 'Summarize', createdAt: '2026-10-16T06:00:00Z', tokenUsage: null,
    attachments: [{ fileId: 'f-1', fileName: 'sales.xlsx', fileExtension: '.xlsx', fileSize: 7139 }],
  };

  async function render(message: ChatMessage): Promise<HTMLElement> {
    fixture = TestBed.createComponent(ChatMessageView);
    fixture.componentRef.setInput('message', message);
    fixture.componentRef.setInput('conversationTitle', 'Sales');
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
});
