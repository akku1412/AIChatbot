import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { NotificationService } from '../../shared/components/toast/notification.service';
import { CustomInstructions } from './custom-instructions';

describe('CustomInstructions', () => {
  let fixture: ComponentFixture<CustomInstructions>;
  let http: HttpTestingController;
  let toasts: string[];

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    toasts = [];
    const notifications = TestBed.inject(NotificationService);
    vi.spyOn(notifications, 'success').mockImplementation((m: string) => void toasts.push(`ok: ${m}`));
    vi.spyOn(notifications, 'error').mockImplementation((m: string) => void toasts.push(`error: ${m}`));
    fixture = TestBed.createComponent(CustomInstructions);
    await fixture.whenStable();
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  const el = () => fixture.nativeElement as HTMLElement;
  const box = (id: string) => el().querySelector(`#${id}`) as HTMLTextAreaElement;
  const button = (label: string) =>
    Array.from(el().querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent?.trim() === label)!;

  function type(id: string, value: string): void {
    box(id).value = value;
    box(id).dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function loaded(aboutMe: string | null, responseStyle: string | null): void {
    http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/users/me/settings')).flush({ aboutMe, responseStyle });
    fixture.detectChanges();
  }

  it('loads the saved instructions; Save is enabled only after a change', () => {
    loaded('I teach chemistry.', null);
    expect(box('about-me').value).toBe('I teach chemistry.');
    expect(box('response-style').value).toBe('');
    expect(button('Save').disabled).toBe(true);

    type('response-style', 'Use bullet points.');
    expect(button('Save').disabled).toBe(false);
    expect(el().querySelector('#response-style-count')?.textContent).toBe('18/1500');

    button('Discard changes').click();
    fixture.detectChanges();
    expect(box('response-style').value).toBe('');
    expect(button('Save').disabled).toBe(true);
  });

  it('saves both fields and confirms', () => {
    loaded(null, null);
    type('about-me', 'I am a .NET developer.');
    button('Save').click();

    const put = http.expectOne((r) => r.method === 'PUT' && r.url.endsWith('/users/me/settings'));
    expect(put.request.body).toEqual({ aboutMe: 'I am a .NET developer.', responseStyle: '' });
    put.flush({ aboutMe: 'I am a .NET developer.', responseStyle: null });
    fixture.detectChanges();

    expect(toasts).toEqual(['ok: Custom instructions saved. They apply from your next message.']);
    expect(button('Save').disabled).toBe(true);
  });

  it("shows the server's reason when saving fails, keeping the text", () => {
    loaded(null, null);
    type('about-me', 'x');
    button('Save').click();
    http.expectOne((r) => r.method === 'PUT').flush(
      { title: 'Validation failed', errors: { AboutMe: ['"About you" must be 1500 characters or fewer.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();
    expect(toasts.length).toBe(1);
    expect(toasts[0]).toContain('error:');
    expect(box('about-me').value).toBe('x');
  });

  it('caps each box at 1,500 characters', () => {
    loaded(null, null);
    expect(box('about-me').getAttribute('maxlength')).toBe('1500');
    expect(box('response-style').getAttribute('maxlength')).toBe('1500');
  });

  it('offers a retry when loading fails', () => {
    http.expectOne((r) => r.url.endsWith('/users/me/settings')).flush(null, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(el().querySelector('[role="alert"]')?.textContent).toContain("Couldn't load your custom instructions.");
    button('Try again').click();
    loaded('back', null);
    expect(box('about-me').value).toBe('back');
  });
});
