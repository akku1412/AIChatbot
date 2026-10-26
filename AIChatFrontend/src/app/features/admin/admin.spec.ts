import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { environment } from '../../../environments/environment';
import { User } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { adminGuard } from '../../core/guards/auth.guard';
import { authInterceptor } from '../../core/interceptors/auth.interceptor';
import { AdminUser, AdminUsage as Usage } from '../../core/services/admin-api.service';
import { ConfirmDialogService } from '../../shared/components/confirm-dialog/confirm-dialog.service';
import { NotificationService } from '../../shared/components/toast/notification.service';
import { AdminFeedbackPage } from './admin-feedback';
import { AdminUsage } from './admin-usage';
import { AdminUsers } from './admin-users';

const api = environment.apiBaseUrl;

const me: User = {
  userId: 1,
  fullName: 'Ada Admin',
  email: 'ada@example.test',
  profileImageUrl: null,
  isEmailVerified: true,
  hasGoogleAccount: false,
  createdAt: '2026-10-01T00:00:00Z',
  role: 'Admin',
};

function adminUser(overrides: Partial<AdminUser>): AdminUser {
  return {
    userId: 2,
    fullName: 'Bob',
    email: 'bob@example.test',
    role: 'User',
    isBlocked: false,
    createdAt: '2026-10-02T00:00:00Z',
    messagesToday: 3,
    tokensToday: 1234,
    uploadsToday: 1,
    lastActiveOn: '2026-10-26T00:00:00Z',
    ...overrides,
  };
}

function setUp(): { http: HttpTestingController; toasts: string[] } {
  TestBed.configureTestingModule({
    providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()],
  });
  const toasts: string[] = [];
  const notifications = TestBed.inject(NotificationService);
  vi.spyOn(notifications, 'success').mockImplementation((m: string) => void toasts.push(`ok: ${m}`));
  vi.spyOn(notifications, 'error').mockImplementation((m: string) => void toasts.push(`error: ${m}`));
  return { http: TestBed.inject(HttpTestingController), toasts };
}

const text = (fixture: ComponentFixture<unknown>) => (fixture.nativeElement as HTMLElement).textContent ?? '';
const button = (fixture: ComponentFixture<unknown>, label: string) =>
  Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button')).find(
    (b) => b.textContent?.trim() === label,
  );

afterEach(() => vi.restoreAllMocks());

describe('AdminUsers', () => {
  it('lists accounts with today’s usage and offers Block only for other non-admin users', async () => {
    const { http } = setUp();
    TestBed.inject(AuthService).updateUser(me);
    const fixture = TestBed.createComponent(AdminUsers);

    const request = http.expectOne((r) => r.url === `${api}/admin/users`);
    expect(request.request.params.get('page')).toBe('1');
    request.flush({
      items: [adminUser({}), adminUser({ userId: 1, fullName: 'Ada Admin', email: me.email, role: 'Admin' })],
      total: 2,
      page: 1,
      pageSize: 25,
    });
    fixture.detectChanges();
    await fixture.whenStable();

    expect(text(fixture)).toContain('bob@example.test');
    expect(text(fixture)).toContain('1,234');
    expect(text(fixture)).toContain('Admin');
    expect(fixture.nativeElement.querySelectorAll('button.danger-outline').length).toBe(1);
    http.verify();
  });

  it('blocks after confirmation and shows the new state', async () => {
    const { http, toasts } = setUp();
    TestBed.inject(AuthService).updateUser(me);
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
    const fixture = TestBed.createComponent(AdminUsers);
    http.expectOne(`${api}/admin/users?page=1&pageSize=25`).flush({ items: [adminUser({})], total: 1, page: 1, pageSize: 25 });
    fixture.detectChanges();

    button(fixture, 'Block')!.click();
    await fixture.whenStable();
    const block = http.expectOne(`${api}/admin/users/2/block`);
    expect(block.request.method).toBe('POST');
    block.flush(adminUser({ isBlocked: true }));
    fixture.detectChanges();

    expect(text(fixture)).toContain('Blocked');
    expect(button(fixture, 'Unblock')).toBeTruthy();
    expect(toasts).toEqual(['ok: bob@example.test is blocked.']);
    http.verify();
  });

  it('searches after typing stops', async () => {
    vi.useFakeTimers();
    try {
      const { http } = setUp();
      const fixture = TestBed.createComponent(AdminUsers);
      http.expectOne(`${api}/admin/users?page=1&pageSize=25`).flush({ items: [], total: 0, page: 1, pageSize: 25 });
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input.search') as HTMLInputElement;
      input.value = 'bob';
      input.dispatchEvent(new Event('input'));
      input.value = 'bob@';
      input.dispatchEvent(new Event('input'));
      http.expectNone((r) => r.params.has('search'));
      vi.advanceTimersByTime(300);

      http.expectOne(`${api}/admin/users?page=1&pageSize=25&search=bob@`).flush({ items: [], total: 0, page: 1, pageSize: 25 });
      http.verify();
    } finally {
      vi.useRealTimers();
    }
  });
});

describe('AdminUsage', () => {
  const usage: Usage = {
    from: '2026-10-20T00:00:00Z',
    to: '2026-10-26T00:00:00Z',
    days: [
      { date: '2026-10-25T00:00:00Z', activeUsers: 1, messages: 2, inputTokens: 100, outputTokens: 100, uploads: 0, uploadBytes: 0, estimatedCost: 0.0001 },
      { date: '2026-10-26T00:00:00Z', activeUsers: 2, messages: 5, inputTokens: 300, outputTokens: 100, uploads: 1, uploadBytes: 10, estimatedCost: 0.0002 },
    ],
    totals: { activeUsers: 2, messages: 7, inputTokens: 400, outputTokens: 200, uploads: 1, uploadBytes: 10, estimatedCost: 0.0003 },
    topUsers: [{ userId: 2, fullName: 'Bob', email: 'bob@example.test', messages: 7, tokens: 600, estimatedCost: 0.0003 }],
    limits: { dailyTokenQuota: 200000, dailyUploadQuota: 50, inputTokenPricePerMillion: 0.15, outputTokenPricePerMillion: 0.6 },
  };

  it('shows totals, scaled daily bars, top users and the quotas', () => {
    const { http } = setUp();
    const fixture = TestBed.createComponent(AdminUsage);
    http.expectOne(`${api}/admin/usage?days=30`).flush(usage);
    fixture.detectChanges();

    expect(text(fixture)).toContain('600'); // total tokens
    expect(text(fixture)).toContain('$0.0003');
    expect(text(fixture)).toContain('bob@example.test');
    expect(text(fixture)).toContain('200,000 tokens');
    const bars = Array.from(fixture.nativeElement.querySelectorAll('.bar')) as HTMLElement[];
    expect(bars.map((b) => b.style.height)).toEqual(['50%', '100%']);
    http.verify();
  });

  it('reloads for another period', () => {
    const { http } = setUp();
    const fixture = TestBed.createComponent(AdminUsage);
    http.expectOne(`${api}/admin/usage?days=30`).flush(usage);
    fixture.detectChanges();

    const select = fixture.nativeElement.querySelector('select') as HTMLSelectElement;
    select.value = '7';
    select.dispatchEvent(new Event('change'));
    http.expectOne(`${api}/admin/usage?days=7`).flush(usage);
    http.verify();
  });
});

describe('AdminFeedbackPage', () => {
  it('shows ratings with comment, question and reply, and filters by rating', () => {
    const { http } = setUp();
    const fixture = TestBed.createComponent(AdminFeedbackPage);
    http.expectOne(`${api}/admin/feedback?page=1&pageSize=25`).flush({
      items: [
        {
          feedbackId: 9, rating: -1, comment: 'Too vague', createdAt: '2026-10-26T10:00:00Z', userId: 2,
          userEmail: 'bob@example.test', messageId: 40, modelId: 'smart', question: 'Explain DNS', reply: 'DNS is…',
        },
      ],
      total: 1, page: 1, pageSize: 25, helpful: 3, notHelpful: 1,
    });
    fixture.detectChanges();

    expect(text(fixture)).toContain('“Too vague”');
    expect(text(fixture)).toContain('Explain DNS');
    expect(text(fixture)).toContain('3 helpful');

    const select = fixture.nativeElement.querySelector('select') as HTMLSelectElement;
    select.value = 'down';
    select.dispatchEvent(new Event('change'));
    http.expectOne(`${api}/admin/feedback?page=1&pageSize=25&rating=-1`).flush({
      items: [], total: 0, page: 1, pageSize: 25, helpful: 3, notHelpful: 1,
    });
    http.verify();
  });
});

describe('admin access', () => {
  it('adminGuard lets admins in and sends everyone else to the chat', () => {
    setUp();
    const auth = TestBed.inject(AuthService);
    const run = () => TestBed.runInInjectionContext(() => adminGuard({} as never, {} as never));

    auth.updateUser({ ...me, role: 'User' });
    expect(TestBed.inject(Router).serializeUrl(run() as never)).toBe('/chat');

    auth.updateUser(me);
    expect(run()).toBe(true);
  });

  it('a 403 account_blocked response signs the user out with an explanation', () => {
    const { http } = setUp();
    const auth = TestBed.inject(AuthService);
    auth.updateUser({ ...me, role: 'User' });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    TestBed.inject(HttpClient).get(`${api}/conversations`).subscribe({ error: () => undefined });
    http.expectOne(`${api}/conversations`).flush(
      { title: 'Account blocked', detail: 'Your account has been blocked.', reason: 'account_blocked' },
      { status: 403, statusText: 'Forbidden' },
    );

    expect(auth.user()).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { blocked: 1 } });
    http.verify();
  });
});
