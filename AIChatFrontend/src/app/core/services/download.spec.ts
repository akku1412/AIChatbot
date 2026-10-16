import { HttpHeaders, HttpResponse } from '@angular/common/http';

import { fileNameFromDisposition, safeFileName, saveResponse } from './download';

describe('download helpers', () => {
  it('makes titles safe for file names', () => {
    expect(safeFileName('Trip / Plan: Goa?')).toBe('Trip Plan Goa');
    expect(safeFileName('  <>|  ')).toBe('Chat');
    expect(safeFileName('हिंदी चैट')).toBe('हिंदी चैट');
    expect(safeFileName('x'.repeat(100)).length).toBe(60);
  });

  it('reads the file name from Content-Disposition, preferring filename*', () => {
    expect(
      fileNameFromDisposition(
        `attachment; filename="AIChat - x.txt"; filename*=UTF-8''AIChat%20-%20%E0%A4%B9%E0%A4%BF.txt`,
        'fallback.txt',
      ),
    ).toBe('AIChat - हि.txt');
    expect(fileNameFromDisposition('attachment; filename="plain.txt"', 'fallback.txt')).toBe('plain.txt');
    expect(fileNameFromDisposition(null, 'fallback.txt')).toBe('fallback.txt');
  });

  it('saves an API file response through a temporary link, under the server-chosen name', () => {
    const blob = new Blob(['%PDF-1.7'], { type: 'application/pdf' });
    let saved: Blob | undefined;
    const create = vi.spyOn(URL, 'createObjectURL').mockImplementation((b) => {
      saved = b as Blob;
      return 'blob:test';
    });
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    let clicked: HTMLAnchorElement | undefined;
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      clicked = this;
    });

    saveResponse(
      new HttpResponse({
        body: blob,
        headers: new HttpHeaders({ 'Content-Disposition': `attachment; filename*=UTF-8''AIChat%20reply.pdf` }),
      }),
      'fallback.pdf',
    );

    expect(clicked?.download).toBe('AIChat reply.pdf');
    expect(clicked?.href).toBe('blob:test');
    expect(saved).toBe(blob);
    expect(document.querySelector('a[download]')).toBeNull(); // link removed again, no navigation

    create.mockRestore();
    revoke.mockRestore();
    click.mockRestore();
  });
});
