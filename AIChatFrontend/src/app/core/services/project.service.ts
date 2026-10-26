import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, finalize, tap } from 'rxjs';

import { environment } from '../../../environments/environment';
import { errorMessage } from '../http/api-error';
import { Project, ProjectDetail } from '../models/chat.models';

/** The signed-in user's projects (/api/projects) and the sidebar's list of them. */
@Injectable({ providedIn: 'root' })
export class ProjectService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiBaseUrl}/projects`;

  private readonly list = signal<Project[]>([]);
  /** A–Z, as the API returns them. */
  readonly projects = this.list.asReadonly();
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.http
      .get<Project[]>(this.url)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (projects) => this.list.set(projects),
        error: (err: unknown) => this.error.set(errorMessage(err)),
      });
  }

  get(projectId: number): Observable<ProjectDetail> {
    return this.http.get<ProjectDetail>(`${this.url}/${projectId}`);
  }

  create(name: string, instructions: string | null = null): Observable<Project> {
    return this.http.post<Project>(this.url, { name, instructions }).pipe(tap((project) => this.put(project)));
  }

  /** Renames and replaces the instructions (blank clears them). */
  update(projectId: number, name: string, instructions: string | null): Observable<Project> {
    return this.http.put<Project>(`${this.url}/${projectId}`, { name, instructions }).pipe(tap((project) => this.put(project)));
  }

  /** Deletes the project; the server moves its chats back to the main list and keeps its files. */
  delete(projectId: number): Observable<void> {
    return this.http
      .delete<void>(`${this.url}/${projectId}`)
      .pipe(tap(() => this.list.update((list) => list.filter((p) => p.projectId !== projectId))));
  }

  addFile(projectId: number, fileId: string): Observable<ProjectDetail> {
    return this.http.post<ProjectDetail>(`${this.url}/${projectId}/files`, { fileId }).pipe(tap((detail) => this.syncCounts(detail)));
  }

  removeFile(projectId: number, fileId: string): Observable<ProjectDetail> {
    return this.http.delete<ProjectDetail>(`${this.url}/${projectId}/files/${fileId}`).pipe(tap((detail) => this.syncCounts(detail)));
  }

  /** Keeps the sidebar's chat count in step when chats move in or out (without reloading every project). */
  adjustConversationCount(projectId: number, delta: number): void {
    this.list.update((list) =>
      list.map((p) => (p.projectId === projectId ? { ...p, conversationCount: Math.max(0, p.conversationCount + delta) } : p)),
    );
  }

  reset(): void {
    this.list.set([]);
    this.error.set(null);
  }

  private put(project: Project): void {
    this.list.update((list) =>
      [...list.filter((p) => p.projectId !== project.projectId), project].sort(
        (a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }) || a.projectId - b.projectId,
      ),
    );
  }

  private syncCounts(detail: ProjectDetail): void {
    this.list.update((list) =>
      list.map((p) =>
        p.projectId === detail.projectId ? { ...p, fileCount: detail.files.length, conversationCount: detail.conversationCount } : p,
      ),
    );
  }
}
