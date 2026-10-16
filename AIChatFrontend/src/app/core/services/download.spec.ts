import { fileNameFromDisposition, fileTimestamp, safeFileName, saveTextFile } from './download';

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

  it('formats a local timestamp for file names', () => {
    expect(fileTimestamp(new Date(2026, 9, 6, 9, 5))).toBe('2026-10-06 0905');
  });

  it('downloads text through a temporary link with CRLF line breaks and a BOM', async () => {
    let saved: Blob | undefined;
    const create = vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      saved = blob as Blob;
      return 'blob:test';
    });
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    let clicked: HTMLAnchorElement | undefined;
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      clicked = this;
    });

    saveTextFile('line one\nline two', 'reply.txt');

    expect(clicked?.download).toBe('reply.txt');
    expect(clicked?.href).toBe('blob:test');
    expect(document.querySelector('a[download]')).toBeNull(); // link removed again
    const bytes = new Uint8Array(await saved!.arrayBuffer());
    expect(Array.from(bytes.slice(0, 3))).toEqual([0xef, 0xbb, 0xbf]);
    expect(new TextDecoder().decode(bytes.slice(3))).toBe('line one\r\nline two');
    expect(saved!.type).toBe('text/plain;charset=utf-8');

    create.mockRestore();
    revoke.mockRestore();
    click.mockRestore();
  });
});
