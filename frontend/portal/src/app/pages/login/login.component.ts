import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'wm-login',
  imports: [FormsModule],
  template: `
    <div class="min-h-screen grid lg:grid-cols-[1.1fr_1fr]">
      <!-- brand panel: the pulse, before you even log in -->
      <div class="hidden lg:flex flex-col justify-between p-12 bg-surface border-r border-line relative overflow-hidden">
        <div class="absolute inset-0 opacity-[0.04] pointer-events-none"
             style="background-image: radial-gradient(rgb(var(--wm-pulse)) 1px, transparent 1px); background-size: 28px 28px;"></div>
        <div class="font-display text-lg font-bold tracking-tight">
          WM<span class="text-pulse">.</span>
        </div>
        <div>
          <h1 class="font-display text-5xl font-bold leading-[1.05] tracking-tight max-w-md">
            Your workforce,<br />
            <span class="text-pulse">live</span> on one screen.
          </h1>
          <p class="mt-6 text-muted max-w-sm leading-relaxed">
            Time &amp; attendance, scheduling, and payroll exports —
            streamed in real time from every site and every device.
          </p>
        </div>
        <div class="flex items-center gap-3 text-xs text-muted font-mono">
          <span class="inline-block w-2 h-2 rounded-full bg-pulse animate-pulse-ring"></span>
          SYSTEM OPERATIONAL
        </div>
      </div>

      <!-- form panel -->
      <div class="flex items-center justify-center p-8">
        <form class="w-full max-w-sm rise" (ngSubmit)="submit()">
          <div class="lg:hidden font-display text-2xl font-bold mb-10">WM<span class="text-pulse">.</span></div>
          <h2 class="font-display text-2xl font-bold tracking-tight">Sign in</h2>
          <p class="mt-1 text-muted text-sm">Operations console access</p>

          <label class="block mt-8 text-xs font-medium uppercase tracking-widest text-muted" for="user">User</label>
          <input id="user" name="user" autocomplete="username" required
                 class="mt-2 w-full bg-raised border border-line rounded-md px-3.5 py-2.5 text-sm
                        placeholder:text-muted/50 transition-colors focus:border-pulse/60"
                 placeholder="admin" [(ngModel)]="userName" />

          <label class="block mt-5 text-xs font-medium uppercase tracking-widest text-muted" for="pass">Password</label>
          <input id="pass" name="pass" type="password" autocomplete="current-password" required
                 class="mt-2 w-full bg-raised border border-line rounded-md px-3.5 py-2.5 text-sm
                        placeholder:text-muted/50 transition-colors focus:border-pulse/60"
                 placeholder="••••••••" [(ngModel)]="password" />

          @if (error()) {
            <p class="mt-4 text-coral text-sm" role="alert">{{ error() }}</p>
          }

          <button type="submit" [disabled]="busy()"
                  class="mt-8 w-full bg-pulse text-ink font-display font-bold rounded-md py-2.5
                         transition-all hover:brightness-110 active:scale-[0.99]
                         disabled:opacity-50 disabled:pointer-events-none">
            {{ busy() ? 'Signing in…' : 'Enter console' }}
          </button>
        </form>
      </div>
    </div>
  `,
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  userName = '';
  password = '';
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  async submit(): Promise<void> {
    if (!this.userName || !this.password || this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.auth.login(this.userName, this.password);
      await this.router.navigateByUrl('/dashboard');
    } catch {
      this.error.set('Sign-in failed. Check your credentials.');
    } finally {
      this.busy.set(false);
    }
  }
}
