export interface ServerSentEvent {
  event: string;
  data: string;
}

/**
 * Incremental Server-Sent Events parser. Network chunks can end anywhere (mid-line, mid-event), so text is
 * buffered until a blank line completes an event. Supports "event:" and (multi-line) "data:" fields.
 */
export class SseParser {
  private buffer = '';

  /** Feeds newly received text; returns the events it completed. */
  push(chunk: string): ServerSentEvent[] {
    this.buffer += chunk.replace(/\r\n?/g, '\n');
    const events: ServerSentEvent[] = [];
    let end: number;
    while ((end = this.buffer.indexOf('\n\n')) >= 0) {
      const block = this.buffer.slice(0, end);
      this.buffer = this.buffer.slice(end + 2);
      const parsed = parseBlock(block);
      if (parsed) {
        events.push(parsed);
      }
    }
    return events;
  }
}

function parseBlock(block: string): ServerSentEvent | null {
  let event = 'message';
  const data: string[] = [];
  for (const line of block.split('\n')) {
    if (line.startsWith(':')) {
      continue; // comment / keep-alive
    }
    const colon = line.indexOf(':');
    const field = colon < 0 ? line : line.slice(0, colon);
    const value = colon < 0 ? '' : line.slice(colon + 1).replace(/^ /, '');
    if (field === 'event') {
      event = value;
    } else if (field === 'data') {
      data.push(value);
    }
  }
  return data.length ? { event, data: data.join('\n') } : null;
}
