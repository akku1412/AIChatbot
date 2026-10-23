import { SseParser } from './sse-parser';

describe('SseParser', () => {
  it('parses complete events', () => {
    const events = new SseParser().push('event: delta\ndata: {"text":"Hi"}\n\nevent: done\ndata: {}\n\n');
    expect(events).toEqual([
      { event: 'delta', data: '{"text":"Hi"}' },
      { event: 'done', data: '{}' },
    ]);
  });

  it('waits for the rest of an event split across chunks', () => {
    const parser = new SseParser();
    expect(parser.push('event: del')).toEqual([]);
    expect(parser.push('ta\ndata: {"te')).toEqual([]);
    expect(parser.push('xt":"a"}\n')).toEqual([]);
    expect(parser.push('\nevent: delta\ndata: {"text":"b"}\n\n')).toEqual([
      { event: 'delta', data: '{"text":"a"}' },
      { event: 'delta', data: '{"text":"b"}' },
    ]);
  });

  it('handles CRLF, comments, multi-line data and the default event name', () => {
    const events = new SseParser().push(': keep-alive\r\n\r\ndata: line 1\r\ndata: line 2\r\n\r\n');
    expect(events).toEqual([{ event: 'message', data: 'line 1\nline 2' }]);
  });
});
