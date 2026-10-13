import { ComponentFixture, TestBed } from '@angular/core/testing';

import { MessageInput } from './message-input';

describe('MessageInput', () => {
  let fixture: ComponentFixture<MessageInput>;
  let textarea: HTMLTextAreaElement;
  let sent: string[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [MessageInput] }).compileComponents();
    fixture = TestBed.createComponent(MessageInput);
    sent = [];
    fixture.componentInstance.send.subscribe((text) => sent.push(text));
    await fixture.whenStable();
    textarea = fixture.nativeElement.querySelector('textarea');
  });

  function type(value: string): void {
    textarea.value = value;
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function press(init: KeyboardEventInit): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true, ...init });
    textarea.dispatchEvent(event);
    return event;
  }

  it('sends trimmed text on Enter', () => {
    type('  hello  ');
    const event = press({});
    expect(event.defaultPrevented).toBe(true);
    expect(sent).toEqual(['hello']);
  });

  it('does not send on Shift+Enter (new line instead)', () => {
    type('line one');
    const event = press({ shiftKey: true });
    expect(event.defaultPrevented).toBe(false);
    expect(sent).toEqual([]);
  });

  it('does not send while an IME composition is active', () => {
    type('namaste');
    press({ isComposing: true });
    expect(sent).toEqual([]);
  });

  it('does not send blank text or while busy', () => {
    type('   ');
    press({});
    fixture.componentRef.setInput('busy', true);
    type('hi');
    press({});
    expect(sent).toEqual([]);
  });
});
