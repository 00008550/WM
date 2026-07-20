import { Component, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { RealtimeService } from '../core/realtime/realtime.service';

@Component({
  selector: 'wm-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="min-h-screen grid grid-cols-[220px_1fr]">
      <!-- rail -->
      <aside class="bg-surface border-r border-line flex flex-col">
        <div class="px-5 h-14 flex items-center font-display text-lg font-bold tracking-tight border-b border-line">
          WM<span class="text-pulse">.</span>
        </div>

        <nav class="flex-1 py-4 px-2 space-y-0.5">
          @for (item of nav; track item.path) {
            <a [routerLink]="item.path" routerLinkActive="!text-text !bg-raised nav-active"
               class="relative flex items-center gap-3 px-3 py-2 rounded-md text-muted text-sm
                      transition-colors hover:text-text hover:bg-raised/60">
              <span class="font-mono text-xs w-4 text-center opacity-70">{{ item.glyph }}</span>
              {{ item.label }}
            </a>
          }
        </nav>

        <div class="p-4 border-t border-line">
          <div class="flex items-center gap-2 text-xs font-mono"
               [class.text-pulse]="realtime.connected()" [class.text-muted]="!realtime.connected()">
            <span class="inline-block w-1.5 h-1.5 rounded-full"
                  [class.bg-pulse]="realtime.connected()" [class.bg-muted]="!realtime.connected()"
                  [class.animate-pulse-ring]="realtime.connected()"></span>
            {{ realtime.connected() ? 'LIVE' : 'POLLING' }}
          </div>
          <div class="mt-3 flex items-center justify-between">
            <div class="text-sm truncate" [title]="auth.displayName()">{{ auth.displayName() }}</div>
            <button (click)="auth.logout()" title="Sign out"
                    class="text-muted hover:text-coral transition-colors text-xs font-mono">⏻</button>
          </div>
          <button (click)="toggleTheme()"
                  class="mt-3 w-full text-left text-xs text-muted hover:text-text transition-colors font-mono">
            {{ dark() ? '◐ light mode' : '◑ dark mode' }}
          </button>
        </div>
      </aside>

      <main class="min-w-0 overflow-y-auto">
        <router-outlet />
      </main>
    </div>
  `,
  styles: `
    .nav-active::before {
      content: '';
      position: absolute;
      left: -8px;
      top: 20%;
      bottom: 20%;
      width: 2px;
      background: rgb(var(--wm-pulse));
      border-radius: 2px;
    }
  `,
})
export class ShellComponent {
  readonly auth = inject(AuthService);
  readonly realtime = inject(RealtimeService);

  readonly dark = signal(document.documentElement.dataset['theme'] !== 'light');

  readonly nav = [
    { path: '/dashboard', label: 'Dashboard', glyph: '◉' },
    { path: '/employees', label: 'Employees', glyph: '⧉' },
  ];

  constructor() {
    void this.realtime.connect();
  }

  toggleTheme(): void {
    const next = this.dark() ? 'light' : 'dark';
    document.documentElement.dataset['theme'] = next;
    this.dark.set(next === 'dark');
  }
}
