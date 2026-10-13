import { Component, input } from '@angular/core';

/** Original AIChat mark (a speech bubble with a spark); not based on any third-party brand. */
@Component({
  selector: 'app-logo',
  template: `
    <svg [attr.width]="size()" [attr.height]="size()" viewBox="0 0 48 48" aria-hidden="true">
      <rect width="48" height="48" rx="12" fill="var(--accent)" />
      <path d="M14 16a4 4 0 0 1 4-4h12a4 4 0 0 1 4 4v10a4 4 0 0 1-4 4h-7l-6 5v-5h-3a4 4 0 0 1-4-4z"
            fill="none" stroke="var(--accent-contrast)" stroke-width="2.5" stroke-linejoin="round" />
      <path d="M24 16.5l1.4 3.6 3.6 1.4-3.6 1.4L24 26.5l-1.4-3.6-3.6-1.4 3.6-1.4z" fill="var(--accent-contrast)" />
    </svg>
  `,
  styles: `:host { display: inline-flex; }`,
})
export class AppLogo {
  readonly size = input(48);
}
