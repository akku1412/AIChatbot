import { Component, input } from '@angular/core';

@Component({
  selector: 'app-loading-spinner',
  template: `<span class="spinner" [style.width.px]="size()" [style.height.px]="size()" role="status" [attr.aria-label]="label()"></span>`,
  styles: `
    :host { display: inline-flex; }
    .spinner {
      display: inline-block; border-radius: 50%;
      border: 2px solid var(--border); border-top-color: var(--accent);
      animation: spin 0.8s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }
    @media (prefers-reduced-motion: reduce) { .spinner { animation-duration: 2s; } }
  `,
})
export class LoadingSpinner {
  readonly size = input(20);
  readonly label = input('Loading');
}
