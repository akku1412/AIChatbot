import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { ProjectDetail } from '../../core/models/chat.models';
import { ChatStore } from '../../core/services/chat.store';
import { ConfirmDialogService } from '../../shared/components/confirm-dialog/confirm-dialog.service';
import { NotificationService } from '../../shared/components/toast/notification.service';
import { ProjectPage } from './project-page';

describe('ProjectPage', () => {
  let fixture: ComponentFixture<ProjectPage>;
  let http: HttpTestingController;
  let router: Router;
  let toasts: string[];

  const detail = (extra: Partial<ProjectDetail> = {}): ProjectDetail => ({
    projectId: 3, name: 'Launch', instructions: 'Answer as the launch coordinator.', conversationCount: 1,
    files: [{
      fileId: 'f1', fileName: 'plan.pdf', fileExtension: '.pdf', contentType: 'application/pdf', fileSize: 2048,
      status: 'completed', statusMessage: null, pageCount: 2, extractedCharacters: 900, createdAt: '',
    }],
    createdAt: '', updatedAt: '', ...extra,
  });
  const chat = { conversationId: 7, title: 'Venue options', createdAt: '', updatedAt: '', isPinned: false, archivedAt: null, projectId: 3 };

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    toasts = [];
    const notifications = TestBed.inject(NotificationService);
    vi.spyOn(notifications, 'success').mockImplementation((m: string) => void toasts.push(m));
    vi.spyOn(notifications, 'error').mockImplementation((m: string) => void toasts.push(`error: ${m}`));
    fixture = TestBed.createComponent(ProjectPage);
    fixture.componentRef.setInput('projectId', '3');
    await fixture.whenStable();
    http.match((r) => r.url.endsWith('/files/limits')); // keep the defaults
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  const el = () => fixture.nativeElement as HTMLElement;
  const button = (label: string) =>
    Array.from(el().querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent?.trim() === label)!;

  function loaded(d: ProjectDetail = detail()): void {
    http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/projects/3')).flush(d);
    http.expectOne((r) => r.params.get('projectId') === '3').flush({ items: [chat], page: 1, pageSize: 100, totalCount: 1, hasMore: false });
    fixture.detectChanges();
  }

  it('shows the project, its files and its chats', () => {
    loaded();
    expect(el().querySelector('h1')?.textContent).toBe('Launch');
    expect((el().querySelector('#project-instructions') as HTMLTextAreaElement).value).toBe('Answer as the launch coordinator.');
    expect(el().querySelector('.files app-attachment-chip .name')?.textContent).toBe('plan.pdf');
    expect(el().querySelector('.chats a')?.textContent).toBe('Venue options');
  });

  it('saves the name and instructions; blank instructions are sent as null', () => {
    loaded();
    expect(button('Save').disabled).toBe(true);
    const box = el().querySelector('#project-instructions') as HTMLTextAreaElement;
    box.value = '   ';
    box.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    button('Save').click();

    const put = http.expectOne((r) => r.method === 'PUT' && r.url.endsWith('/projects/3'));
    expect(put.request.body).toEqual({ name: 'Launch', instructions: null });
    put.flush({ projectId: 3, name: 'Launch', instructions: null, fileCount: 1, conversationCount: 1, createdAt: '', updatedAt: '' });
    fixture.detectChanges();
    expect(toasts[0]).toContain('Project saved');
    expect(button('Save').disabled).toBe(true);
  });

  it('removes a shared file from the project', () => {
    loaded();
    button('Remove').click();
    http.expectOne((r) => r.method === 'DELETE' && r.url.endsWith('/projects/3/files/f1')).flush(detail({ files: [] }));
    fixture.detectChanges();
    expect(el().querySelector('.files app-attachment-chip')).toBeNull();
    expect(el().textContent).toContain('No files yet.');
  });

  it('moves a chat out of the project', () => {
    loaded();
    button('Move out').click();
    const put = http.expectOne((r) => r.method === 'PUT' && r.url.endsWith('/conversations/7/project'));
    expect(put.request.body).toEqual({ projectId: null });
    put.flush({ ...chat, projectId: null });
    fixture.detectChanges();
    expect(el().querySelector('.chats')).toBeNull();
    expect(TestBed.inject(ChatStore).conversations().map((c) => c.conversationId)).toEqual([7]);
  });

  it('deletes the project after confirming, then returns to the chat list', async () => {
    loaded();
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
    const projectDeleted = vi.spyOn(TestBed.inject(ChatStore), 'projectDeleted').mockImplementation(() => undefined);

    button('Delete project').click();
    await fixture.whenStable();
    http.expectOne((r) => r.method === 'DELETE' && r.url.endsWith('/projects/3')).flush(null);

    expect(projectDeleted).toHaveBeenCalledWith(3);
    expect(router.navigate).toHaveBeenCalledWith(['/chat']);
  });

  it('keeps the project when deletion is cancelled', async () => {
    loaded();
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(false);
    button('Delete project').click();
    await fixture.whenStable();
    http.expectNone((r) => r.method === 'DELETE');
  });

  it("explains when the project doesn't exist (or isn't yours)", () => {
    http.expectOne((r) => r.url.endsWith('/projects/3')).flush(
      { title: 'Project not found', detail: "This project doesn't exist or has been deleted." },
      { status: 404, statusText: 'Not Found' },
    );
    http.expectOne((r) => r.params.get('projectId') === '3').flush(null, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(el().querySelector('[role="alert"]')?.textContent).toContain("This project doesn't exist or has been deleted.");
  });
});
