import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Inline SVG icon set, 1.5px stroke, 24px grid — no icon font, no emoji.
 * Paths drawn for the Control Room aesthetic: geometric, slightly technical.
 */
@Component({
  selector: 'wm-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg [attr.width]="size()" [attr.height]="size()" viewBox="0 0 24 24" fill="none"
         stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"
         aria-hidden="true">
      @switch (name()) {
        @case ('pulse') {
          <path d="M2 12h4l3-8 4 16 3-8h6" />
        }
        @case ('people') {
          <circle cx="9" cy="8" r="3.25" />
          <path d="M3.5 19.5c.7-3 2.9-4.5 5.5-4.5s4.8 1.5 5.5 4.5" />
          <path d="M15.5 5.4a3.25 3.25 0 1 1 .6 6" />
          <path d="M17.2 15.4c1.9.5 3 1.9 3.3 4.1" />
        }
        @case ('power') {
          <path d="M12 3v7" />
          <path d="M6.5 6.5a7.5 7.5 0 1 0 11 0" />
        }
        @case ('sun') {
          <circle cx="12" cy="12" r="4" />
          <path d="M12 2.5v2M12 19.5v2M21.5 12h-2M4.5 12h-2M18.7 5.3l-1.4 1.4M6.7 17.3l-1.4 1.4M18.7 18.7l-1.4-1.4M6.7 6.7L5.3 5.3" />
        }
        @case ('moon') {
          <path d="M20.5 14.5A8.5 8.5 0 1 1 9.5 3.5a7 7 0 0 0 11 11Z" />
        }
        @case ('arrow-in') {
          <path d="M9 12h11" />
          <path d="M16 8l4 4-4 4" />
          <path d="M4 5v14" />
        }
        @case ('arrow-out') {
          <path d="M15 12H4" />
          <path d="M8 8l-4 4 4 4" />
          <path d="M20 5v14" />
        }
        @case ('search') {
          <circle cx="11" cy="11" r="6.5" />
          <path d="M16 16l5 5" />
        }
        @case ('chevron-left') {
          <path d="M14.5 6l-6 6 6 6" />
        }
        @case ('chevron-right') {
          <path d="M9.5 6l6 6-6 6" />
        }
      }
    </svg>
  `,
})
export class IconComponent {
  readonly name = input.required<string>();
  readonly size = input(18);
}
