import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AiModels } from '../../../core/models/chat.models';
import { ChatStore } from '../../../core/services/chat.store';
import { NotificationService } from '../toast/notification.service';
import { ModelPicker } from './model-picker';

describe('ModelPicker', () => {
  let fixture: ComponentFixture<ModelPicker>;
  let http: HttpTestingController;
  let store: ChatStore;
  let errors: string[];

  const twoModels: AiModels = {
    defaultModelId: 'smart',
    models: [
      { id: 'fast', label: 'Fast', description: 'Quick answers', supportsVision: false },
      { id: 'smart', label: 'Smart', description: 'Better reasoning', supportsVision: false },
    ],
  };

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    store = TestBed.inject(ChatStore);
    errors = [];
    vi.spyOn(TestBed.inject(NotificationService), 'error').mockImplementation((m: string) => void errors.push(m));
    fixture = TestBed.createComponent(ModelPicker);
    await fixture.whenStable();
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  const el = () => fixture.nativeElement as HTMLElement;
  const trigger = () => el().querySelector('button.trigger') as HTMLButtonElement | null;
  const items = () => Array.from(el().querySelectorAll<HTMLButtonElement>('[role="menuitemradio"]'));

  function loadModels(models: AiModels): void {
    http.expectOne((r) => r.url.endsWith('/models')).flush(models);
    fixture.detectChanges();
  }

  it('stays hidden when the server offers a single model', () => {
    loadModels({ defaultModelId: 'default', models: [{ id: 'default', label: 'Default', description: null, supportsVision: false }] });
    expect(trigger()).toBeNull();
  });

  it('shows the default, and for a new chat switches locally without calling the server', () => {
    loadModels(twoModels);
    expect(trigger()?.textContent?.trim()).toBe('Smart');

    trigger()!.click();
    fixture.detectChanges();
    expect(items().map((i) => i.getAttribute('aria-checked'))).toEqual(['false', 'true']);
    expect(items()[0].textContent).toContain('Quick answers');

    items()[0].click();
    fixture.detectChanges();
    expect(trigger()?.textContent?.trim()).toBe('Fast');
    expect(el().querySelector('[role="menu"]')).toBeNull();
    http.expectNone((r) => r.method === 'PATCH');
  });

  it('saves the pick for an open chat, and reports a failure', () => {
    loadModels(twoModels);
    store.activeConversationId.set(7);
    fixture.detectChanges();

    trigger()!.click();
    fixture.detectChanges();
    items()[0].click();
    const patch = http.expectOne((r) => r.method === 'PATCH' && r.url.endsWith('/conversations/7'));
    expect(patch.request.body).toEqual({ modelId: 'fast' });
    patch.flush({ conversationId: 7, title: 'Chat', createdAt: '', updatedAt: '', isPinned: false, archivedAt: null, modelId: 'fast' });
    fixture.detectChanges();
    expect(trigger()?.textContent?.trim()).toBe('Fast');

    trigger()!.click();
    fixture.detectChanges();
    items()[1].click();
    http.expectOne((r) => r.method === 'PATCH').flush({ title: 'Bad request', detail: "This model isn't available." }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(errors).toEqual(["Couldn't change the model. This model isn't available."]);
    expect(trigger()?.textContent?.trim()).toBe('Fast'); // unchanged
  });

  it('is disabled while a reply is being generated', () => {
    loadModels(twoModels);
    store.sending.set(true);
    fixture.detectChanges();
    expect(trigger()?.disabled).toBe(true);
  });
});
