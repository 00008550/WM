import { Component, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { RealtimeService } from '../core/realtime/realtime.service';
import { IconComponent } from '../core/ui/icon.component';

@Component({
  selector: 'wm-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, IconComponent],
  template: `
    <div class="min-h-screen grid grid-cols-[228px_1fr]">
      <!-- rail -->
      <aside class="bg-surface border-r border-line flex flex-col sticky top-0 h-screen">
        <div class="px-5 h-14 flex items-center gap-2 font-display text-lg font-bold tracking-tight border-b border-line">
          WM<span class="text-pulse">.</span>
          <span class="ml-auto font-mono text-[9px] font-normal text-muted tracking-widest uppercase">console</span>
        </div>

        <nav class="flex-1 py-4 px-2.5 space-y-0.5">
          @for (item of nav(); track item.path) {
            <a [routerLink]="item.path" routerLinkActive="!text-text !bg-raised nav-active"
               class="relative flex items-center gap-3 px-3 py-2 rounded-md text-muted text-sm
                      transition-colors duration-150 hover:text-text hover:bg-raised/60">
              <wm-icon [name]="item.icon" [size]="16" />
              {{ item.label }}
            </a>
          }
        </nav>

        <div class="p-4 border-t border-line space-y-3">
          <div class="flex items-center gap-2 text-[10px] font-mono tracking-wider"
               [class.text-pulse]="realtime.connected()" [class.text-muted]="!realtime.connected()"
               role="status" aria-live="polite">
            <span class="inline-block w-1.5 h-1.5 rounded-full"
                  [class.bg-pulse]="realtime.connected()" [class.bg-muted]="!realtime.connected()"
                  [class.animate-pulse-ring]="realtime.connected()"></span>
            {{ realtime.connected() ? 'REALTIME LINK UP' : 'POLLING FALLBACK' }}
          </div>

          <div class="flex items-center justify-between gap-2">
            <span class="text-sm truncate" [title]="auth.displayName()">{{ auth.displayName() }}</span>
            <div class="flex items-center gap-1">
              <button (click)="toggleTheme()" [attr.aria-label]="dark() ? 'Switch to light mode' : 'Switch to dark mode'"
                      class="p-1.5 rounded-md text-muted hover:text-text hover:bg-raised transition-colors duration-150">
                <wm-icon [name]="dark() ? 'sun' : 'moon'" [size]="15" />
              </button>
              <button (click)="auth.logout()" aria-label="Sign out"
                      class="p-1.5 rounded-md text-muted hover:text-coral hover:bg-raised transition-colors duration-150">
                <wm-icon name="power" [size]="15" />
              </button>
            </div>
          </div>
        </div>
      </aside>

      <main class="min-w-0 overflow-y-auto">
        <router-outlet />
      </main>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .nav-active::before {
      content: '';
      position: absolute;
      left: -10px;
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

  // Nav reflects what this user may actually do — an employee-only account sees
  // just "My time"; a manager/admin sees the operations views.
  readonly nav = computed(() => {
    const items: { path: string; label: string; icon: string }[] = [];
    if (this.auth.hasPermission('attendance.view'))
      items.push({ path: '/dashboard', label: 'Dashboard', icon: 'pulse' });
    if (this.auth.hasPermission('employees.view'))
      items.push({ path: '/employees', label: 'Employees', icon: 'people' });
    if (this.auth.isEmployeeLinked())
      items.push({ path: '/me', label: 'My time', icon: 'clock' });
    if (this.auth.hasPermission('users.manage'))
      items.push({ path: '/users', label: 'Users', icon: 'users' });
    if (this.auth.hasPermission('roles.manage'))
      items.push({ path: '/security-groups', label: 'Security groups', icon: 'key' });
    return items;
  });

  constructor() {
    void this.realtime.connect();
  }

  toggleTheme(): void {
    const next = this.dark() ? 'light' : 'dark';
    document.documentElement.dataset['theme'] = next;
    this.dark.set(next === 'dark');
  }
}
