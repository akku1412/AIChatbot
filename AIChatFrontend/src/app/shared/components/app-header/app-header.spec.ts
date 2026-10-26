import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ChatExportFormat } from '../../../core/models/chat.models';
import { AppHeader } from './app-header';

describe('AppHeader', () => {
  let fixture: ComponentFixture<AppHeader>;
  let exported: ChatExportFormat[];

  beforeEach(async () => {
    fixture = TestBed.createComponent(AppHeader);
    fixture.componentRef.setInput('canExport', true);
    exported = [];
    fixture.componentInstance.export.subscribe((format) => exported.push(format));
    await fixture.whenStable();
  });

  const el = () => fixture.nativeElement as HTMLElement;
  const exportButton = () => el().querySelector('button[aria-label="Export chat"]') as HTMLButtonElement;

  it('offers the whole chat as TXT, PDF or Word', () => {
    exportButton().click();
    fixture.detectChanges();
    const items = Array.from(el().querySelectorAll<HTMLButtonElement>('[role="menuitem"]'));
    expect(items.map((b) => b.textContent?.trim())).toEqual(['TXT', 'PDF', 'Word (DOCX)']);

    items[1]!.click();
    fixture.detectChanges();
    expect(exported).toEqual(['pdf']);
    expect(el().querySelector('[role="menu"]')).toBeNull();
  });

  it('closes the menu with Esc or a click elsewhere', () => {
    exportButton().click();
    fixture.detectChanges();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(el().querySelector('[role="menu"]')).toBeNull();

    exportButton().click();
    fixture.detectChanges();
    document.body.click();
    fixture.detectChanges();
    expect(el().querySelector('[role="menu"]')).toBeNull();
    expect(exported).toEqual([]);
  });

  it('hides Share and Export when there is no saved chat', () => {
    fixture.componentRef.setInput('canExport', false);
    fixture.detectChanges();
    expect(exportButton()).toBeNull();
    expect(el().querySelector('button[aria-label="Share chat"]')).toBeNull();
  });
});
