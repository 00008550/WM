import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { SelfServiceApi, TimesheetDay } from '../../core/api/self-service.api';
import { EmployeeRow, PunchRow } from '../../core/api/workforce.api';
import { AuthService } from '../../core/auth/auth.service';
import { IconComponent } from '../../core/ui/icon.component';

@Component({
  selector: 'wm-self-service',
  imports: [DatePipe, DecimalPipe, IconComponent],
  template: `
    <div class="p-8 max-w-4xl">
      <header class="rise" style="--i: 0">
        <h1 class="font-display text-2xl font-bold tracking-tight">My time</h1>
        <p class="text-muted text-sm mt-0.5">
          @if (employee(); as e) {
            {{ e.firstName }} {{ e.lastName }} · <span class="num">{{ e.code }}</span> · {{ e.jobTitle ?? '—' }}
          } @else {
            {{ auth.displayName() }}
          }
        </p>
      </header>

      <!-- clock in / out -->
      <section class="mt-8 rise" style="--i: 1">
        <div class="border border-line rounded-lg bg-surface p-6 flex flex-col sm:flex-row sm:items-center gap-6">
          <div class="flex-1">
            <div class="text-[11px] uppercase tracking-[0.18em] text-muted font-medium">Status</div>
            <div class="mt-1.5 flex items-center gap-2.5">
              <span class="inline-block w-2.5 h-2.5 rounded-full"
                    [class.bg-pulse]="clockedIn()" [class.bg-muted]="!clockedIn()"
                    [class.animate-pulse-ring]="clockedIn()"></span>
              <span class="font-display text-xl font-bold">{{ clockedIn() ? 'Clocked in' : 'Clocked out' }}</span>
              @if (lastPunchTime()) {
                <span class="num text-xs text-muted">since {{ lastPunchTime() | date: 'HH:mm' }}</span>
              }
            </div>
          </div>
          <div class="flex gap-2">
            <button (click)="punch('In')" [disabled]="busy() || clockedIn()"
                    class="flex items-center gap-2 px-4 py-2.5 rounded-md bg-pulse text-ink font-display font-bold text-sm
                           transition-transform duration-100 active:scale-[0.97] disabled:opacity-40">
              <wm-icon name="arrow-in" [size]="16" /> Clock in
            </button>
            <button (click)="punch('Out')" [disabled]="busy() || !clockedIn()"
                    class="flex items-center gap-2 px-4 py-2.5 rounded-md border border-coral/50 text-coral font-display font-bold text-sm
                           transition-all duration-100 hover:bg-coral/10 active:scale-[0.97] disabled:opacity-40">
              <wm-icon name="arrow-out" [size]="16" /> Clock out
            </button>
          </div>
        </div>
        @if (error()) {
          <p class="mt-2 text-coral text-xs" role="alert">{{ error() }}</p>
        }
      </section>

      <div class="grid lg:grid-cols-2 gap-10 mt-10 items-start">
        <!-- this week -->
        <section class="rise" style="--i: 2">
          <div class="flex items-baseline justify-between border-b border-line pb-3">
            <h2 class="font-display font-bold text-sm tracking-wide">This week</h2>
            <span class="num text-xs text-muted">{{ weekTotal() | number: '1.0-1' }}h total</span>
          </div>
          <div class="divide-y divide-line/50">
            @if (loading()) {
              @for (i of [0,1,2,3,4]; track i) {
                <div class="py-3 flex items-center justify-between">
                  <div class="skeleton h-3.5 w-28"></div><div class="skeleton h-3.5 w-10"></div>
                </div>
              }
            } @else {
              @for (day of timesheet(); track day.date) {
                <div class="py-3 flex items-center justify-between">
                  <div>
                    <div class="text-sm">{{ day.date | date: 'EEEE' }}</div>
                    <div class="text-xs text-muted num">{{ day.date | date: 'd MMM' }}</div>
                  </div>
                  <div class="text-right">
                    <div class="num text-sm" [class.text-muted]="day.totalHours === 0">
                      {{ day.totalHours | number: '1.0-2' }}h
                    </div>
                    @if (day.intervals.length) {
                      <div class="num text-[10px] text-muted">
                        {{ day.intervals[0].in | date: 'HH:mm' }}–{{ day.intervals[day.intervals.length-1].out | date: 'HH:mm' }}
                      </div>
                    }
                  </div>
                </div>
              } @empty {
                <div class="py-10 text-center text-muted text-sm">No hours recorded this week yet.</div>
              }
            }
          </div>
        </section>

        <!-- recent punches -->
        <section class="rise" style="--i: 3">
          <h2 class="font-display font-bold text-sm tracking-wide border-b border-line pb-3">Recent punches</h2>
          <div class="divide-y divide-line/50 max-h-[360px] overflow-y-auto">
            @if (loading()) {
              @for (i of [0,1,2,3]; track i) {
                <div class="py-2.5 flex items-center gap-3"><div class="skeleton h-3 w-12"></div><div class="skeleton h-4 w-8"></div><div class="skeleton h-3 flex-1"></div></div>
              }
            } @else {
              @for (p of punches(); track p.id) {
                <div class="py-2.5 flex items-center gap-3 text-sm">
                  <span class="num text-[11px] text-muted w-24">{{ p.timestamp | date: 'd MMM HH:mm' }}</span>
                  <span class="font-mono text-[9px] px-1.5 py-0.5 rounded-sm border"
                        [class]="p.direction === 0 ? 'text-pulse border-pulse/40 bg-pulse/10' : 'text-coral border-coral/40 bg-coral/10'">
                    {{ p.direction === 0 ? 'IN' : 'OUT' }}
                  </span>
                  <span class="text-muted flex-1 truncate">{{ p.deviceId ?? 'Web' }}</span>
                </div>
              } @empty {
                <div class="py-10 text-center text-muted text-sm">No punches yet.</div>
              }
            }
          </div>
        </section>
      </div>
    </div>
  `,
})
export class SelfServiceComponent implements OnInit {
  private readonly api = inject(SelfServiceApi);
  readonly auth = inject(AuthService);

  readonly loading = signal(true);
  readonly employee = signal<EmployeeRow | null>(null);
  readonly punches = signal<PunchRow[]>([]);
  readonly timesheet = signal<TimesheetDay[]>([]);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  // Clocked in if the most recent punch is an In.
  readonly clockedIn = computed(() => this.punches()[0]?.direction === 0);
  readonly lastPunchTime = computed(() => this.punches()[0]?.timestamp ?? null);
  readonly weekTotal = computed(() => this.timesheet().reduce((sum, d) => sum + d.totalHours, 0));

  ngOnInit(): void {
    this.api.myEmployee().subscribe({ next: e => this.employee.set(e), error: () => {} });
    this.reload();
  }

  punch(direction: 'In' | 'Out'): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.api.punchSelf(direction).subscribe({
      next: () => { this.busy.set(false); this.reload(); },
      error: err => { this.busy.set(false); this.error.set(err?.error?.detail ?? 'Could not record your punch.'); },
    });
  }

  private reload(): void {
    this.api.myPunches(20).subscribe(p => { this.punches.set(p); this.loading.set(false); });
    this.api.myTimesheet().subscribe(t => this.timesheet.set(t));
  }
}
