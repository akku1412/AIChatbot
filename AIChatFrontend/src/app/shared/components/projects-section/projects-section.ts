import { Component, ElementRef, inject, output, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { errorMessage } from '../../../core/http/api-error';
import { ConversationSummary, PROJECT_NAME_MAX, Project } from '../../../core/models/chat.models';
import { ChatStore } from '../../../core/services/chat.store';
import { ProjectService } from '../../../core/services/project.service';
import { NotificationService } from '../toast/notification.service';

/**
 * "Projects" in the sidebar: collapsible folders with their chats, "New project", a link to each project's page
 * (instructions and shared files) and "New chat" inside a project.
 */
@Component({
  selector: 'app-projects-section',
  imports: [RouterLink],
  template: `
    <section class="projects" aria-labelledby="projects-label">
      <div class="head">
        <h2 id="projects-label" class="group-label">Projects</h2>
        <button type="button" class="icon" aria-label="New project" title="New project" (click)="startCreate()">
          <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M12 5v14M5 12h14" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>
        </button>
      </div>

      @if (creating()) {
        <input #newName class="name-input" type="text" placeholder="Project name" aria-label="New project name"
               [attr.maxlength]="nameMax" (keydown.enter)="create($event)" (keydown.escape)="creating.set(false)" (blur)="create($event)" />
      }

      @if (projects.error(); as error) {
        <p class="state error">Couldn't load projects. <button type="button" class="link" (click)="projects.load()">Retry</button></p>
      }

      @for (project of projects.projects(); track project.projectId) {
        <div class="folder">
          <button type="button" class="toggle" [attr.aria-expanded]="isOpen(project.projectId)"
                  [attr.aria-controls]="'project-chats-' + project.projectId" (click)="toggle(project)">
            <svg class="chevron" [class.open]="isOpen(project.projectId)" viewBox="0 0 24 24" width="14" height="14" aria-hidden="true"><path d="M9 6l6 6-6 6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>
            <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"/></svg>
            <span class="name" [title]="project.name">{{ project.name }}</span>
          </button>
          <a class="settings" [routerLink]="['/projects', project.projectId]" [attr.aria-label]="'Open project ' + project.name"
             title="Instructions and files" (click)="selected.emit()">
            <svg viewBox="0 0 24 24" width="15" height="15" aria-hidden="true"><path d="M4 6h16M4 12h16M4 18h10" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>
          </a>
        </div>
        @if (isOpen(project.projectId)) {
          <div class="chats" role="list" [id]="'project-chats-' + project.projectId">
            <button type="button" class="new-chat" (click)="newChat(project)">
              <svg viewBox="0 0 24 24" width="14" height="14" aria-hidden="true"><path d="M12 5v14M5 12h14" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>
              New chat in {{ project.name }}
            </button>
            @for (chat of chatsOf(project.projectId); track chat.conversationId) {
              <a role="listitem" class="chat" [class.active]="chat.conversationId === store.activeConversationId()"
                 [attr.aria-current]="chat.conversationId === store.activeConversationId() ? 'page' : null"
                 [routerLink]="['/chat', chat.conversationId]" [title]="chat.title" (click)="selected.emit()">{{ chat.title }}</a>
            } @empty {
              <p class="state">No chats yet.</p>
            }
          </div>
        }
      } @empty {
        @if (!projects.loading() && !creating()) {
          <p class="state">Group chats, files and instructions. <button type="button" class="link" (click)="startCreate()">New project</button></p>
        }
      }
    </section>
  `,
  styles: `
    .projects { padding-bottom: 4px; }
    .head { display: flex; align-items: center; justify-content: space-between; margin: 16px 4px 6px 8px; }
    .group-label { margin: 0; font-size: 0.75rem; font-weight: 600; color: var(--text-muted); }
    .icon {
      width: 26px; height: 26px; display: grid; place-items: center; border: 0; border-radius: 6px;
      background: transparent; color: var(--text-muted); cursor: pointer;
    }
    .icon:hover { background: var(--surface-hover); color: var(--text); }
    .name-input {
      width: 100%; box-sizing: border-box; margin: 2px 0 4px; padding: 6px 10px; border: 1px solid var(--accent);
      border-radius: 8px; background: var(--surface); color: var(--text); font: inherit; font-size: 0.9375rem;
    }
    .folder { display: flex; align-items: center; border-radius: 8px; }
    .folder:hover { background: var(--surface-hover); }
    .toggle {
      flex: 1; min-width: 0; display: flex; align-items: center; gap: 6px; padding: 8px 4px 8px 6px; border: 0;
      background: transparent; color: var(--text); font: inherit; font-size: 0.9375rem; text-align: left; cursor: pointer;
    }
    .chevron { flex: none; color: var(--text-muted); transition: transform 120ms ease; }
    .chevron.open { transform: rotate(90deg); }
    .name { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .settings {
      flex: none; width: 30px; height: 30px; margin-right: 4px; display: grid; place-items: center; border-radius: 6px;
      color: var(--text-muted);
    }
    .settings:hover { background: color-mix(in srgb, var(--text) 8%, transparent); color: var(--text); }
    .chats { margin-left: 18px; padding-left: 6px; border-left: 1px solid var(--border); }
    .chat {
      display: block; padding: 6px 10px; border-radius: 8px; color: var(--text); text-decoration: none; font-size: 0.875rem;
      white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
    }
    .chat:hover { background: var(--surface-hover); }
    .chat.active { background: var(--accent-soft); font-weight: 600; }
    .new-chat {
      display: flex; align-items: center; gap: 6px; width: 100%; padding: 6px 10px; border: 0; border-radius: 8px;
      background: transparent; color: var(--accent); font: inherit; font-size: 0.8125rem; text-align: left; cursor: pointer;
      white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
    }
    .new-chat:hover { background: var(--surface-hover); }
    .state { margin: 4px 10px 8px; font-size: 0.8125rem; color: var(--text-muted); }
    .state.error { color: var(--danger); }
    .link { border: 0; background: none; padding: 0; color: var(--accent); font: inherit; cursor: pointer; text-decoration: underline; }
    @media (prefers-reduced-motion: reduce) { .chevron { transition: none; } }
  `,
})
export class ProjectsSection {
  protected readonly projects = inject(ProjectService);
  protected readonly store = inject(ChatStore);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);

  /** A link was followed (closes the drawer on small screens). */
  readonly selected = output<void>();

  protected readonly nameMax = PROJECT_NAME_MAX;
  protected readonly creating = signal(false);
  private readonly expanded = signal<ReadonlySet<number>>(new Set());
  private readonly newName = viewChild<ElementRef<HTMLInputElement>>('newName');

  constructor() {
    this.projects.load();
  }

  /** The project's loaded chats (empty until its folder has been opened once). */
  protected chatsOf(projectId: number): ConversationSummary[] {
    const lists: Partial<Record<number, ConversationSummary[]>> = this.store.projectConversations();
    return lists[projectId] ?? [];
  }

  protected isOpen(projectId: number): boolean {
    return this.expanded().has(projectId);
  }

  protected toggle(project: Project): void {
    const open = !this.isOpen(project.projectId);
    this.expanded.update((set) => {
      const next = new Set(set);
      if (open) {
        next.add(project.projectId);
      } else {
        next.delete(project.projectId);
      }
      return next;
    });
    if (open) {
      this.store.loadProjectConversations(project.projectId);
    }
  }

  protected startCreate(): void {
    this.creating.set(true);
    setTimeout(() => this.newName()?.nativeElement.focus());
  }

  protected create(event: Event): void {
    if (!this.creating()) {
      return; // Enter already handled it (blur fires as the input is removed)
    }
    event.preventDefault();
    const name = (event.target as HTMLInputElement).value.trim();
    this.creating.set(false);
    if (!name) {
      return;
    }
    this.projects.create(name.slice(0, PROJECT_NAME_MAX)).subscribe({
      next: (project) => {
        this.expanded.update((set) => new Set(set).add(project.projectId));
        this.store.loadProjectConversations(project.projectId);
        this.selected.emit();
        this.router.navigate(['/projects', project.projectId]);
      },
      error: (err: unknown) => this.notifications.error(`Couldn't create the project. ${errorMessage(err)}`),
    });
  }

  protected newChat(project: Project): void {
    this.store.newChatIn(project.projectId);
    this.selected.emit();
    this.router.navigate(['/chat']);
  }
}
