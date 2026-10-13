import { Component } from '@angular/core';

import { AppLogo } from '../app-logo/app-logo';

/** Shown in the thread while the AI is generating a reply. */
@Component({
  selector: 'app-typing-indicator',
  imports: [AppLogo],
  template: `
    <div class="row" role="status" aria-label="AIChat is thinking">
      <app-logo [size]="30" />
      <div class="dots" aria-hidden="true"><span></span><span></span><span></span></div>
    </div>
  `,
  styles: `
    .row { display: flex; gap: 12px; align-items: center; padding: 12px 0; }
    .dots { display: flex; gap: 4px; }
    .dots span { width: 7px; height: 7px; border-radius: 50%; background: var(--accent); animation: pulse 1.2s infinite ease-in-out; }
    .dots span:nth-child(2) { animation-delay: 0.15s; }
    .dots span:nth-child(3) { animation-delay: 0.3s; }
    @keyframes pulse { 0%, 80%, 100% { opacity: 0.25; transform: scale(0.85); } 40% { opacity: 1; transform: scale(1); } }
    @media (prefers-reduced-motion: reduce) { .dots span { animation: none; opacity: 0.6; } }
  `,
})
export class TypingIndicator {}
