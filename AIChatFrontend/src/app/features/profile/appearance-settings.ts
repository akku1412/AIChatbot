import { Component, inject } from '@angular/core';

import { ThemePreference, ThemeService } from '../../core/services/theme.service';

/** Profile card: Light / Dark / System (follow the device). Saved to the account and applied at once. */
@Component({
  selector: 'app-appearance-settings',
  template: `
    <section class="card" aria-labelledby="appearance-title">
      <h3 id="appearance-title">Appearance</h3>
      <div class="choices" role="radiogroup" aria-labelledby="appearance-title">
        @for (option of options; track option.value) {
          <button type="button" role="radio" class="choice" [class.selected]="theme.preference() === option.value"
                  [attr.aria-checked]="theme.preference() === option.value" (click)="theme.set(option.value)">
            <span class="swatch" [class]="option.value" aria-hidden="true"></span>
            <span class="label">{{ option.label }}</span>
            <span class="hint">{{ option.hint }}</span>
          </button>
        }
      </div>
    </section>
  `,
  styles: `
    .choices { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 10px; }
    .choice {
      display: flex; flex-direction: column; align-items: flex-start; gap: 4px; padding: 10px; border: 1px solid var(--border);
      border-radius: 12px; background: var(--surface); color: var(--text); font: inherit; text-align: left; cursor: pointer;
    }
    .choice:hover { border-color: color-mix(in srgb, var(--accent) 40%, var(--border)); }
    .choice.selected { border-color: var(--accent); box-shadow: 0 0 0 1px var(--accent); }
    .choice:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
    .swatch { width: 100%; height: 40px; border-radius: 8px; border: 1px solid var(--border); margin-bottom: 4px; }
    .swatch.light { background: linear-gradient(135deg, #ffffff 50%, #eceef3 50%); }
    .swatch.dark { background: linear-gradient(135deg, #14151a 50%, #262833 50%); }
    .swatch.system { background: linear-gradient(135deg, #ffffff 50%, #14151a 50%); }
    .label { font-weight: 600; font-size: 0.875rem; }
    .hint { font-size: 0.75rem; color: var(--text-muted); }
    @media (max-width: 480px) { .choices { grid-template-columns: 1fr; } }
  `,
  styleUrl: './profile-card.css',
})
export class AppearanceSettings {
  protected readonly theme = inject(ThemeService);

  protected readonly options: { value: ThemePreference; label: string; hint: string }[] = [
    { value: 'system', label: 'System', hint: "Match this device's setting" },
    { value: 'light', label: 'Light', hint: 'Always light' },
    { value: 'dark', label: 'Dark', hint: 'Always dark' },
  ];
}
