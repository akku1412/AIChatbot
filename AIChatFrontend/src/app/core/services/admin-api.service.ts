import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';

/** Mirrors ChatGPTClone.Api DTOs/Admin. Dates are UTC; "days" are UTC days. */
export interface AdminUser {
  userId: number;
  fullName: string;
  email: string;
  role: 'User' | 'Admin';
  isBlocked: boolean;
  createdAt: string;
  messagesToday: number;
  tokensToday: number;
  uploadsToday: number;
  lastActiveOn: string | null;
}

export interface Page<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface UsageDay {
  date: string;
  activeUsers: number;
  messages: number;
  inputTokens: number;
  outputTokens: number;
  uploads: number;
  uploadBytes: number;
  estimatedCost: number;
}

export type UsageTotals = Omit<UsageDay, 'date'>;

export interface TopUser {
  userId: number;
  fullName: string;
  email: string;
  messages: number;
  tokens: number;
  estimatedCost: number;
}

export interface AdminUsage {
  from: string;
  to: string;
  days: UsageDay[];
  totals: UsageTotals;
  topUsers: TopUser[];
  limits: {
    dailyTokenQuota: number;
    dailyUploadQuota: number;
    inputTokenPricePerMillion: number;
    outputTokenPricePerMillion: number;
  };
}

export interface AdminFeedback {
  feedbackId: number;
  rating: 1 | -1;
  comment: string | null;
  createdAt: string;
  userId: number;
  userEmail: string;
  messageId: number;
  modelId: string | null;
  question: string;
  reply: string;
}

export interface AdminFeedbackPage extends Page<AdminFeedback> {
  helpful: number;
  notHelpful: number;
}

/** /api/admin/* — the API refuses every call unless the signed-in account is currently an admin. */
@Injectable({ providedIn: 'root' })
export class AdminApiService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiBaseUrl}/admin`;

  users(search: string, page: number, pageSize = 25): Observable<Page<AdminUser>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (search.trim()) {
      params = params.set('search', search.trim());
    }
    return this.http.get<Page<AdminUser>>(`${this.url}/users`, { params });
  }

  setBlocked(userId: number, blocked: boolean): Observable<AdminUser> {
    return this.http.post<AdminUser>(`${this.url}/users/${userId}/${blocked ? 'block' : 'unblock'}`, {});
  }

  usage(days: number): Observable<AdminUsage> {
    return this.http.get<AdminUsage>(`${this.url}/usage`, { params: { days } });
  }

  feedback(rating: 1 | -1 | null, page: number, pageSize = 25): Observable<AdminFeedbackPage> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (rating !== null) {
      params = params.set('rating', rating);
    }
    return this.http.get<AdminFeedbackPage>(`${this.url}/feedback`, { params });
  }
}
