import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Meta, Title } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';

import { SharedConversation } from '../../core/models/chat.models';
import { SharedChat } from './shared-chat';

describe('SharedChat', () => {
  let fixture: ComponentFixture<SharedChat>;
  let http: HttpTestingController;

  const shared: SharedConversation = {
    title: 'Launch plan',
    sharedAt: '2026-10-26T05:00:00Z',
    messages: [
      { role: 'user', content: 'When is the launch?', createdAt: '', attachments: ['memo.pdf'], isTruncated: false },
      { role: 'assistant', content: 'On **15 December** (memo.pdf, Page 2).', createdAt: '', attachments: [], isTruncated: true },
    ],
  };

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SharedChat);
    fixture.componentRef.setInput('token', 'tok_123');
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  const el = () => fixture.nativeElement as HTMLElement;

  it('shows the read-only snapshot, with file names but no downloads', () => {
    const request = http.expectOne((r) => r.url.endsWith('/share/tok_123'));
    expect(request.request.method).toBe('GET');
    request.flush(shared);
    fixture.detectChanges();

    expect(el().querySelector('h1')?.textContent).toBe('Launch plan');
    expect(el().querySelector('.bubble')?.textContent).toBe('When is the launch?');
    expect(el().querySelector('.files li')?.textContent).toBe('memo.pdf');
    expect(el().querySelector('app-markdown strong')?.textContent).toBe('15 December');
    expect(el().querySelector('.stopped')?.textContent).toBe('(stopped)');
    expect(el().querySelector('textarea, app-message-input, [download], .download')).toBeNull();
    expect(TestBed.inject(Title).getTitle()).toBe('Launch plan · Shared from AIChat');
  });

  it('asks search engines not to index it and hides the token from linked sites', () => {
    http.expectOne((r) => r.url.endsWith('/share/tok_123')).flush(shared);
    const meta = TestBed.inject(Meta);
    expect(meta.getTag("name='robots'")?.content).toBe('noindex, nofollow');
    expect(meta.getTag("name='referrer'")?.content).toBe('no-referrer');

    fixture.destroy();
    expect(meta.getTag("name='robots'")).toBeNull();
  });

  it('explains a revoked or unknown link', () => {
    http.expectOne((r) => r.url.endsWith('/share/tok_123')).flush(
      { title: 'Link not found', detail: "This shared link doesn't exist or is no longer shared." },
      { status: 404, statusText: 'Not Found' },
    );
    fixture.detectChanges();
    expect(el().querySelector('[role="alert"]')?.textContent).toContain('no longer shared');
  });
});
