import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';

import { errorMessage } from '../../core/http/api-error';
import { AdminApiService, AdminUsage as Usage } from '../../core/services/admin-api.service';
import { LoadingSpinner } from '../../shared/components/loading-spinner/loading-spinner';

/** /admin/usage — totals, a daily token chart and table, the heaviest users, and the quotas in force. */
@Component({
  selector: 'app-admin-usage',
  imports: [CurrencyPipe, DatePipe, DecimalPipe, LoadingSpinner],
  template: `
    <div class="toolbar">
      <label class="muted" for="usage-days">Period</label>
      <select id="usage-days" class="select" [value]="days()" (change)="setDays(+$any($event.target).value)">
        @for (option of dayOptions; track option) {
          <option [value]="option">Last {{ option }} days</option>
        }
      </select>
    </div>

    @if (error(); as message) {
      <div class="state error" role="alert">{{ message }}</div>
    } @else if (!usage()) {
      <div class="state"><app-loading-spinner /></div>
    } @else {
      @let u = usage()!;
      <div class="tiles">
        <div class="tile"><span class="label">Active users</span><span class="value">{{ u.totals.activeUsers | number }}</span></div>
        <div class="tile"><span class="label">Messages</span><span class="value">{{ u.totals.messages | number }}</span></div>
        <div class="tile">
          <span class="label">Tokens</span>
          <span class="value">{{ u.totals.inputTokens + u.totals.outputTokens | number }}</span>
          <span class="sub">{{ u.totals.inputTokens | number }} in · {{ u.totals.outputTokens | number }} out</span>
        </div>
        <div class="tile"><span class="label">Uploads</span><span class="value">{{ u.totals.uploads | number }}</span></div>
        <div class="tile">
          <span class="label">Estimated cost</span>
          <span class="value">{{ u.totals.estimatedCost | currency: 'USD' : 'symbol' : '1.2-4' }}</span>
          <span class="sub">at {{ u.limits.inputTokenPricePerMillion | currency: 'USD' }} / {{ u.limits.outputTokenPricePerMillion | currency: 'USD' }} per 1M tokens</span>
        </div>
      </div>

      <section class="card">
        <h3>Tokens per day</h3>
        <div class="chart" role="img" [attr.aria-label]="'Tokens per day, ' + (u.from | date: 'mediumDate' : 'UTC') + ' to ' + (u.to | date: 'mediumDate' : 'UTC')">
          @for (day of u.days; track day.date) {
            <div class="bar" [style.height.%]="barHeight(day.inputTokens + day.outputTokens)"
                 [title]="(day.date | date: 'mediumDate' : 'UTC') + ': ' + (day.inputTokens + day.outputTokens | number) + ' tokens'"></div>
          }
        </div>
        <p class="muted quota">
          Daily limits per user:
          {{ u.limits.dailyTokenQuota > 0 ? (u.limits.dailyTokenQuota | number) + ' tokens' : 'no token limit' }},
          {{ u.limits.dailyUploadQuota > 0 ? (u.limits.dailyUploadQuota | number) + ' uploads' : 'no upload limit' }}.
        </p>
      </section>

      <h3 class="section-title">Top users</h3>
      @if (u.topUsers.length === 0) {
        <div class="state">No usage in this period.</div>
      } @else {
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">Account</th>
                <th scope="col" class="num">Messages</th>
                <th scope="col" class="num">Tokens</th>
                <th scope="col" class="num">Est. cost</th>
              </tr>
            </thead>
            <tbody>
              @for (user of u.topUsers; track user.userId) {
                <tr>
                  <td><div class="who"><span>{{ user.fullName }}</span><span class="sub">{{ user.email }}</span></div></td>
                  <td class="num">{{ user.messages | number }}</td>
                  <td class="num">{{ user.tokens | number }}</td>
                  <td class="num">{{ user.estimatedCost | currency: 'USD' : 'symbol' : '1.2-4' }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }

      <h3 class="section-title">By day</h3>
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th scope="col">Day (UTC)</th>
              <th scope="col" class="num">Active users</th>
              <th scope="col" class="num">Messages</th>
              <th scope="col" class="num">Input tokens</th>
              <th scope="col" class="num">Output tokens</th>
              <th scope="col" class="num">Uploads</th>
              <th scope="col" class="num">Est. cost</th>
            </tr>
          </thead>
          <tbody>
            @for (day of daysNewestFirst(); track day.date) {
              <tr>
                <td>{{ day.date | date: 'EEE, d MMM' : 'UTC' }}</td>
                <td class="num">{{ day.activeUsers | number }}</td>
                <td class="num">{{ day.messages | number }}</td>
                <td class="num">{{ day.inputTokens | number }}</td>
                <td class="num">{{ day.outputTokens | number }}</td>
                <td class="num">{{ day.uploads | number }}</td>
                <td class="num">{{ day.estimatedCost | currency: 'USD' : 'symbol' : '1.2-4' }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
  styleUrls: ['../profile/profile-card.css', './admin.css'],
  styles: `
    .tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 12px; }
    .tile {
      display: flex; flex-direction: column; gap: 2px; padding: 14px 16px;
      border: 1px solid var(--border); border-radius: var(--radius-lg); background: var(--surface-raised);
    }
    .tile .label { color: var(--text-muted); font-size: 0.8rem; font-weight: 600; }
    .tile .value { font-size: 1.375rem; font-weight: 700; font-variant-numeric: tabular-nums; }
    .tile .sub { color: var(--text-muted); font-size: 0.75rem; }
    .chart { display: flex; align-items: flex-end; gap: 3px; height: 140px; padding-top: 8px; }
    .bar { flex: 1; min-height: 2px; border-radius: 3px 3px 0 0; background: var(--accent); }
    .bar:hover { background: var(--accent-hover); }
    .quota { margin: 12px 0 0; }
    .section-title { margin: 8px 0 0; font-size: 1rem; }
  `,
})
export class AdminUsage {
  private readonly api = inject(AdminApiService);

  protected readonly dayOptions = [7, 30, 90];
  protected readonly days = signal(30);
  protected readonly usage = signal<Usage | null>(null);
  protected readonly error = signal<string | null>(null);

  protected readonly daysNewestFirst = computed(() => [...(this.usage()?.days ?? [])].reverse());
  private readonly maxTokens = computed(() =>
    Math.max(0, ...(this.usage()?.days ?? []).map((d) => d.inputTokens + d.outputTokens)),
  );

  constructor() {
    this.load();
  }

  protected setDays(days: number): void {
    this.days.set(days);
    this.load();
  }

  protected barHeight(tokens: number): number {
    const max = this.maxTokens();
    return max === 0 ? 0 : (tokens / max) * 100;
  }

  private load(): void {
    const days = this.days();
    this.usage.set(null);
    this.error.set(null);
    this.api.usage(days).subscribe({
      next: (usage) => {
        if (days === this.days()) {
          this.usage.set(usage);
        }
      },
      error: (err: unknown) => this.error.set(errorMessage(err)),
    });
  }
}
