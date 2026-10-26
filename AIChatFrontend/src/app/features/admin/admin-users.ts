import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';

import { AuthService } from '../../core/auth/auth.service';
import { errorMessage } from '../../core/http/api-error';
import { AdminApiService, AdminUser } from '../../core/services/admin-api.service';
import { ConfirmDialogService } from '../../shared/components/confirm-dialog/confirm-dialog.service';
import { LoadingSpinner } from '../../shared/components/loading-spinner/loading-spinner';
import { NotificationService } from '../../shared/components/toast/notification.service';

const PAGE_SIZE = 25;

/** /admin/users — search accounts, see today's usage, block and unblock. */
@Component({
  selector: 'app-admin-users',
  imports: [DatePipe, DecimalPipe, LoadingSpinner],
  template: `
    <div class="toolbar">
      <input class="search" type="search" placeholder="Search by email or name" aria-label="Search users"
             [value]="search()" (input)="onSearch($any($event.target).value)" />
      <span class="muted">{{ total() | number }} {{ total() === 1 ? 'account' : 'accounts' }}</span>
    </div>

    @if (error(); as message) {
      <div class="state error" role="alert">{{ message }}</div>
    } @else if (loading() && users().length === 0) {
      <div class="state"><app-loading-spinner /></div>
    } @else if (users().length === 0) {
      <div class="state">No accounts match.</div>
    } @else {
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th scope="col">Account</th>
              <th scope="col">Status</th>
              <th scope="col" class="num">Messages today</th>
              <th scope="col" class="num">Tokens today</th>
              <th scope="col" class="num">Uploads today</th>
              <th scope="col">Last active</th>
              <th scope="col">Joined</th>
              <th scope="col" aria-label="Actions"></th>
            </tr>
          </thead>
          <tbody>
            @for (user of users(); track user.userId) {
              <tr>
                <td>
                  <div class="who">
                    <span>{{ user.fullName }}</span>
                    <span class="sub">{{ user.email }}</span>
                  </div>
                </td>
                <td>
                  @if (user.role === 'Admin') { <span class="badge admin">Admin</span> }
                  @if (user.isBlocked) { <span class="badge blocked">Blocked</span> }
                  @else if (user.role !== 'Admin') { <span class="badge">Active</span> }
                </td>
                <td class="num">{{ user.messagesToday | number }}</td>
                <td class="num">{{ user.tokensToday | number }}</td>
                <td class="num">{{ user.uploadsToday | number }}</td>
                <td>{{ user.lastActiveOn ? (user.lastActiveOn | date: 'mediumDate' : 'UTC') : '—' }}</td>
                <td>{{ user.createdAt | date: 'mediumDate' }}</td>
                <td>
                  @if (user.isBlocked) {
                    <button type="button" class="btn small secondary" [disabled]="busyId() === user.userId" (click)="unblock(user)">Unblock</button>
                  } @else if (user.role !== 'Admin' && user.userId !== myId()) {
                    <button type="button" class="btn small danger-outline" [disabled]="busyId() === user.userId" (click)="block(user)">Block</button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      @if (pageCount() > 1) {
        <div class="pager">
          <button type="button" class="btn small secondary" [disabled]="page() === 1 || loading()" (click)="goTo(page() - 1)">Previous</button>
          <span class="muted">Page {{ page() }} of {{ pageCount() }}</span>
          <button type="button" class="btn small secondary" [disabled]="page() === pageCount() || loading()" (click)="goTo(page() + 1)">Next</button>
        </div>
      }
    }
  `,
  styleUrls: ['../profile/profile-card.css', './admin.css'],
})
export class AdminUsers {
  private readonly api = inject(AdminApiService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly notifications = inject(NotificationService);

  private readonly auth = inject(AuthService);

  protected readonly myId = computed(() => this.auth.user()?.userId ?? null);
  protected readonly search = signal('');
  protected readonly page = signal(1);
  protected readonly users = signal<AdminUser[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly busyId = signal<number | null>(null);
  protected readonly pageCount = computed(() => Math.max(1, Math.ceil(this.total() / PAGE_SIZE)));

  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.searchTimer));
    this.load();
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.goTo(1), 300);
  }

  protected goTo(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected async block(user: AdminUser): Promise<void> {
    const confirmed = await this.confirmDialog.confirm({
      title: `Block ${user.fullName}?`,
      message: `${user.email} will be signed out and won't be able to sign in or use the app until you unblock the account.`,
      confirmText: 'Block',
      danger: true,
    });
    if (confirmed) {
      this.setBlocked(user, true);
    }
  }

  protected unblock(user: AdminUser): void {
    this.setBlocked(user, false);
  }

  private setBlocked(user: AdminUser, blocked: boolean): void {
    this.busyId.set(user.userId);
    this.api.setBlocked(user.userId, blocked).subscribe({
      next: (updated) => {
        this.busyId.set(null);
        this.users.update((list) => list.map((u) => (u.userId === updated.userId ? updated : u)));
        this.notifications.success(blocked ? `${updated.email} is blocked.` : `${updated.email} is unblocked.`);
      },
      error: (err: unknown) => {
        this.busyId.set(null);
        this.notifications.error(errorMessage(err));
      },
    });
  }

  private load(): void {
    const page = this.page();
    const search = this.search();
    this.loading.set(true);
    this.error.set(null);
    this.api.users(search, page, PAGE_SIZE).subscribe({
      next: (result) => {
        // A slower, older request must not overwrite a newer search.
        if (page !== this.page() || search !== this.search()) {
          return;
        }
        this.users.set(result.items);
        this.total.set(result.total);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(errorMessage(err));
        this.loading.set(false);
      },
    });
  }
}
