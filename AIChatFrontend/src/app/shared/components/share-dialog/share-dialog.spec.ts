import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { NotificationService } from '../toast/notification.service';
import { ShareDialog } from './share-dialog';

describe('ShareDialog', () => {
  let fixture: ComponentFixture<ShareDialog>;
  let http: HttpTestingController;
  let toasts: string[];
  let closed: number;

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    toasts = [];
    closed = 0;
    const notifications = TestBed.inject(NotificationService);
    vi.spyOn(notifications, 'success').mockImplementation((m: string) => void toasts.push(m));
    vi.spyOn(notifications, 'error').mockImplementation((m: string) => void toasts.push(`error: ${m}`));
    fixture = TestBed.createComponent(ShareDialog);
    fixture.componentRef.setInput('conversationId', 7);
    fixture.componentInstance.closed.subscribe(() => closed++);
    await fixture.whenStable();
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  const el = () => fixture.nativeElement as HTMLElement;
  const button = (label: string) =>
    Array.from(el().querySelectorAll<HTMLButtonElement>('.actions button')).find((b) => b.textContent?.trim() === label);

  function status(isShared: boolean): void {
    http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/conversations/7/share'))
      .flush({ isShared, sharedAt: isShared ? '2026-10-26T05:00:00Z' : null, messageCount: isShared ? 4 : null });
    fixture.detectChanges();
  }

  it('explains what sharing does, then creates a link and shows it once', async () => {
    status(false);
    expect(el().textContent).toContain('Messages you send after sharing stay private.');
    expect(button('Stop sharing')).toBeUndefined();

    button('Create link')!.click();
    const post = http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/conversations/7/share'));
    post.flush({ token: 'abc_DEF-123', sharedAt: '2026-10-26T05:00:00Z', messageCount: 4 });
    fixture.detectChanges();

    const input = el().querySelector('input[aria-label="Share link"]') as HTMLInputElement;
    expect(input.value).toBe(`${window.location.origin}/share/abc_DEF-123`);
    expect(el().textContent).toContain("this link can't be shown again");
    expect(button('Create new link')).toBeDefined();
  });

  it('for a chat shared earlier, offers a replacement link or stopping', () => {
    status(true);
    expect(el().textContent).toContain('4 messages');
    expect(el().querySelector('input[aria-label="Share link"]')).toBeNull(); // the old link can't be shown

    button('Stop sharing')!.click();
    http.expectOne((r) => r.method === 'DELETE' && r.url.endsWith('/conversations/7/share')).flush(null);
    fixture.detectChanges();
    expect(toasts).toEqual(['Sharing stopped. The link no longer works.']);
    expect(button('Create link')).toBeDefined();
  });

  it("reports when a chat can't be shared yet", () => {
    status(false);
    button('Create link')!.click();
    http.expectOne((r) => r.method === 'POST').flush(
      { title: 'Nothing to share yet', detail: "Send a message first: an empty chat can't be shared." },
      { status: 409, statusText: 'Conflict' },
    );
    expect(toasts[0]).toContain("an empty chat can't be shared");
  });

  it('closes with Esc', () => {
    status(false);
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(closed).toBe(1);
  });
});
