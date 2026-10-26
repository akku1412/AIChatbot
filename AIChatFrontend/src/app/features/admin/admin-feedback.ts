import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';

import { errorMessage } from '../../core/http/api-error';
import { AdminApiService, AdminFeedback } from '../../core/services/admin-api.service';
import { LoadingSpinner } from '../../shared/components/loading-spinner/loading-spinner';

const PAGE_SIZE = 25;

type RatingFilter = 'all' | 'up' | 'down';

/** /admin/feedback — thumbs up/down on AI replies, with the question, the reply and the comment. */
@Component({
  selector: 'app-admin-feedback',
  imports: [DatePipe, DecimalPipe, LoadingSpinner],
  template: `
    <div class="toolbar">
      <label class="muted" for="feedback-filter">Show</label>
      <select id="feedback-filter" class="select" [value]="filter()" (change)="setFilter($any($event.target).value)">
        <option value="all">All ratings</option>
        <option value="down">Not helpful</option>
        <option value="up">Helpful</option>
      </select>
      <span class="grow"></span>
      <span class="badge up">{{ helpful() | number }} helpful</span>
      <span class="badge down">{{ notHelpful() | number }} not helpful</span>
    </div>

    @if (error(); as message) {
      <div class="state error" role="alert">{{ message }}</div>
    } @else if (loading() && items().length === 0) {
      <div class="state"><app-loading-spinner /></div>
    } @else if (items().length === 0) {
      <div class="state">No feedback yet.</div>
    } @else {
      <ul class="list">
        @for (item of items(); track item.feedbackId) {
          <li class="card item">
            <div class="meta">
              <span class="badge" [class.up]="item.rating > 0" [class.down]="item.rating < 0">{{ item.rating > 0 ? 'Helpful' : 'Not helpful' }}</span>
              <span class="muted">{{ item.userEmail }}</span>
              @if (item.modelId) { <span class="muted">· {{ item.modelId }}</span> }
              <span class="grow"></span>
              <span class="muted">{{ item.createdAt | date: 'medium' }}</span>
            </div>
            @if (item.comment) {
              <p class="comment">“{{ item.comment }}”</p>
            }
            <dl>
              <dt>Question</dt>
              <dd>{{ item.question || '—' }}</dd>
              <dt>Reply</dt>
              <dd>{{ item.reply }}</dd>
            </dl>
          </li>
        }
      </ul>
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
  styles: `
    .list { margin: 0; padding: 0; list-style: none; display: flex; flex-direction: column; gap: 12px; }
    .meta { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; }
    .grow { flex: 1; }
    .comment { margin: 10px 0 0; font-weight: 500; }
    dl { margin: 10px 0 0; display: grid; grid-template-columns: auto 1fr; gap: 6px 12px; font-size: 0.875rem; }
    dt { color: var(--text-muted); font-weight: 600; }
    dd { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; }
    @media (max-width: 600px) { dl { grid-template-columns: 1fr; } }
  `,
})
export class AdminFeedbackPage {
  private readonly api = inject(AdminApiService);

  protected readonly filter = signal<RatingFilter>('all');
  protected readonly page = signal(1);
  protected readonly items = signal<AdminFeedback[]>([]);
  protected readonly total = signal(0);
  protected readonly helpful = signal(0);
  protected readonly notHelpful = signal(0);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly pageCount = computed(() => Math.max(1, Math.ceil(this.total() / PAGE_SIZE)));

  constructor() {
    this.load();
  }

  protected setFilter(filter: RatingFilter): void {
    this.filter.set(filter);
    this.goTo(1);
  }

  protected goTo(page: number): void {
    this.page.set(page);
    this.load();
  }

  private load(): void {
    const page = this.page();
    const filter = this.filter();
    this.loading.set(true);
    this.error.set(null);
    this.api.feedback(filter === 'all' ? null : filter === 'up' ? 1 : -1, page, PAGE_SIZE).subscribe({
      next: (result) => {
        if (page !== this.page() || filter !== this.filter()) {
          return;
        }
        this.items.set(result.items);
        this.total.set(result.total);
        this.helpful.set(result.helpful);
        this.notHelpful.set(result.notHelpful);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(errorMessage(err));
        this.loading.set(false);
      },
    });
  }
}
