import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { Project } from '../../../core/models/chat.models';
import { ChatStore } from '../../../core/services/chat.store';
import { ProjectsSection } from './projects-section';

describe('ProjectsSection', () => {
  let fixture: ComponentFixture<ProjectsSection>;
  let http: HttpTestingController;
  let router: Router;

  const project = (projectId: number, name: string): Project => ({
    projectId, name, instructions: null, fileCount: 0, conversationCount: 0, createdAt: '', updatedAt: '',
  });
  const chat = (id: number, title: string) => ({
    conversationId: id, title, createdAt: '', updatedAt: '2026-10-26T00:00:00Z', isPinned: false, archivedAt: null, projectId: 3,
  });

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(ProjectsSection);
    await fixture.whenStable();
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  const el = () => fixture.nativeElement as HTMLElement;

  function loaded(projects: Project[]): void {
    http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/projects')).flush(projects);
    fixture.detectChanges();
  }

  it('invites to create a project when there are none', () => {
    loaded([]);
    expect(el().textContent).toContain('Group chats, files and instructions.');
  });

  it('opening a folder loads its chats and offers a new chat inside the project', () => {
    loaded([project(3, 'Thesis')]);
    const toggle = el().querySelector('button.toggle') as HTMLButtonElement;
    expect(toggle.getAttribute('aria-expanded')).toBe('false');

    toggle.click();
    fixture.detectChanges();
    const list = http.expectOne((r) => r.url.endsWith('/conversations') && r.params.get('projectId') === '3');
    expect(list.request.params.get('archived')).toBe('false');
    list.flush({ items: [chat(7, 'Chapter 1 outline')], page: 1, pageSize: 100, totalCount: 1, hasMore: false });
    fixture.detectChanges();

    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(el().querySelector('a.chat')?.textContent?.trim()).toBe('Chapter 1 outline');

    (el().querySelector('button.new-chat') as HTMLButtonElement).click();
    expect(TestBed.inject(ChatStore).newChatProject()).toBe(3);
    expect(router.navigate).toHaveBeenCalledWith(['/chat']);
  });

  it('creates a project from the inline name box and opens its page', () => {
    loaded([]);
    (el().querySelector('button.icon') as HTMLButtonElement).click();
    fixture.detectChanges();
    const input = el().querySelector('input.name-input') as HTMLInputElement;
    input.value = '  Launch plan  ';
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    fixture.detectChanges();

    const post = http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/projects'));
    expect(post.request.body).toEqual({ name: 'Launch plan', instructions: null });
    post.flush(project(4, 'Launch plan'));
    http.expectOne((r) => r.params.get('projectId') === '4').flush({ items: [], page: 1, pageSize: 100, totalCount: 0, hasMore: false });
    fixture.detectChanges();

    expect(el().querySelector('.name')?.textContent).toBe('Launch plan');
    expect(router.navigate).toHaveBeenCalledWith(['/projects', 4]);
  });

  it('a blank name creates nothing', () => {
    loaded([]);
    (el().querySelector('button.icon') as HTMLButtonElement).click();
    fixture.detectChanges();
    const input = el().querySelector('input.name-input') as HTMLInputElement;
    input.value = '   ';
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    http.expectNone((r) => r.method === 'POST');
  });
});
