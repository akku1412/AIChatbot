import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { MessageSource } from '../../../core/models/chat.models';
import { SourcePreviewService } from '../../../core/services/source-preview.service';
import { SourcePanel } from './source-panel';

describe('SourcePanel', () => {
  let fixture: ComponentFixture<SourcePanel>;
  let http: HttpTestingController;
  let previews: SourcePreviewService;
  const scanned: MessageSource = { chunkId: 12, fileId: 'f-1', fileName: 'memo.pdf', location: 'Page 3', isOcr: true };

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    previews = TestBed.inject(SourcePreviewService);
    fixture = TestBed.createComponent(SourcePanel);
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  const el = () => fixture.nativeElement as HTMLElement;

  it('is hidden until a source is shown, then loads and shows the passage as text', async () => {
    expect(el().querySelector('[role="dialog"]')).toBeNull();

    previews.show(scanned);
    fixture.detectChanges();
    expect(el().querySelector('h2')?.textContent).toBe('memo.pdf');
    expect(el().querySelector('.where')?.textContent).toContain('Page 3');
    expect(el().querySelector('.tag')?.textContent).toBe('Scanned (OCR)');
    expect(el().textContent).toContain('Loading passage…');

    http.expectOne((r) => r.url.endsWith('/files/f-1/chunks/12')).flush({
      chunkId: 12, fileId: 'f-1', fileName: 'memo.pdf', location: 'Page 3',
      content: 'The deadline is <b>15 December</b>.', source: 'ocr', isSummary: false,
    });
    fixture.detectChanges();
    const passage = el().querySelector('.passage mark') as HTMLElement;
    expect(passage.textContent).toBe('The deadline is <b>15 December</b>.');
    expect(passage.querySelector('b')).toBeNull(); // shown as text, never as HTML
  });

  it("explains when the passage can't be loaded (deleted file, or not the user's)", () => {
    previews.show(scanned);
    fixture.detectChanges();
    http.expectOne((r) => r.url.endsWith('/files/f-1/chunks/12')).flush(
      { title: 'Not found', detail: 'This passage is no longer available.' },
      { status: 404, statusText: 'Not Found' },
    );
    fixture.detectChanges();
    expect(el().querySelector('[role="alert"]')?.textContent).toContain('This passage is no longer available.');
  });

  it('closes with Esc or the Close button and cancels a pending load', () => {
    previews.show(scanned);
    fixture.detectChanges();
    const pending = http.expectOne((r) => r.url.endsWith('/files/f-1/chunks/12'));

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(previews.source()).toBeNull();
    expect(el().querySelector('[role="dialog"]')).toBeNull();
    expect(pending.cancelled).toBe(true);

    previews.show(scanned);
    fixture.detectChanges();
    http.expectOne((r) => r.url.endsWith('/files/f-1/chunks/12'));
    (el().querySelector('button.close') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el().querySelector('[role="dialog"]')).toBeNull();
  });
});
