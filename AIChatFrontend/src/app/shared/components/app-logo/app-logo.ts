import { Component, input } from '@angular/core';

let nextId = 0;

/** Original AIChat mark (a speech bubble with a spark on an indigo-violet tile); not based on any third-party brand. */
@Component({
  selector: 'app-logo',
  template: `
    <svg [attr.width]="size()" [attr.height]="size()" viewBox="0 0 48 48" aria-hidden="true">
      <defs>
        <linearGradient [attr.id]="gradientId" x1="0" y1="0" x2="48" y2="48" gradientUnits="userSpaceOnUse">
          <stop offset="0" stop-color="#6366f1" />
          <stop offset="0.55" stop-color="#8b5cf6" />
          <stop offset="1" stop-color="#a855f7" />
        </linearGradient>
      </defs>
      <rect width="48" height="48" rx="13" [attr.fill]="'url(#' + gradientId + ')'" />
      <path d="M14 16a4 4 0 0 1 4-4h12a4 4 0 0 1 4 4v10a4 4 0 0 1-4 4h-7l-6 5v-5h-3a4 4 0 0 1-4-4z"
            fill="none" stroke="#fff" stroke-width="2.5" stroke-linejoin="round" />
      <path d="M24 16.5l1.4 3.6 3.6 1.4-3.6 1.4L24 26.5l-1.4-3.6-3.6-1.4 3.6-1.4z" fill="#fff" />
    </svg>
  `,
  styles: `:host { display: inline-flex; flex: none; }`,
})
export class AppLogo {
  readonly size = input(48);
  /** Unique per instance, so several logos on one page don't share a gradient id. */
  protected readonly gradientId = `aichat-logo-${nextId++}`;
}
