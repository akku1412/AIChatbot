import { HttpErrorResponse } from '@angular/common/http';

import { errorMessage, safeReturnUrl, toApiProblem } from './api-error';

describe('safeReturnUrl', () => {
  it('keeps same-app relative paths', () => {
    expect(safeReturnUrl('/chat')).toBe('/chat');
    expect(safeReturnUrl('/chat/42?x=1')).toBe('/chat/42?x=1');
  });

  it('rejects absolute and protocol-relative URLs (open redirect)', () => {
    expect(safeReturnUrl('https://evil.example')).toBe('/chat');
    expect(safeReturnUrl('//evil.example')).toBe('/chat');
    expect(safeReturnUrl('/\\evil.example')).toBe('/chat');
    expect(safeReturnUrl('javascript:alert(1)')).toBe('/chat');
  });

  it('falls back when missing', () => {
    expect(safeReturnUrl(undefined)).toBe('/chat');
    expect(safeReturnUrl('')).toBe('/chat');
  });
});

describe('toApiProblem / errorMessage', () => {
  it('uses the API ProblemDetails detail', () => {
    const err = new HttpErrorResponse({
      status: 400,
      error: { title: 'Incorrect code', detail: 'You have 4 attempts left.', attemptsRemaining: 4 },
    });
    expect(toApiProblem(err).attemptsRemaining).toBe(4);
    expect(errorMessage(err)).toBe('You have 4 attempts left.');
  });

  it('prefers the first validation error', () => {
    const err = new HttpErrorResponse({
      status: 400,
      error: { title: 'Please correct the highlighted fields', errors: { Code: ['The code must be exactly 6 digits.'] } },
    });
    expect(errorMessage(err)).toBe('The code must be exactly 6 digits.');
  });

  it('keeps API ProblemDetails even on 502-504 (AI provider errors)', () => {
    const err = new HttpErrorResponse({
      status: 504,
      error: { title: 'AI took too long', detail: 'The AI took too long to respond.', reason: 'ai_timeout' },
    });
    expect(errorMessage(err)).toBe('The AI took too long to respond.');
  });

  it('maps network and gateway failures to a connection message', () => {
    expect(toApiProblem(new HttpErrorResponse({ status: 0 })).title).toBe('Connection problem');
    expect(toApiProblem(new HttpErrorResponse({ status: 504, error: 'proxy error' })).title).toBe('Connection problem');
  });
});
