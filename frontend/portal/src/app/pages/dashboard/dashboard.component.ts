import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, effect, inject, signal } from '@angular/core';
import { WorkforceApi, LivePresence, PunchRow } from '../../core/api/workforce.api';
import { RealtimeService, PunchEvent } from '../../core/realtime/realtime.service';

interface FeedEntry {
  key: string;
  name: string;
  code: string;
  time: string;
  direction: 'In' | 'Out';
  fresh: boolean;
}

@Component({
  selector: 'wm-dashboard',
  imports: [DatePipe],
  template: `
    <div class="p-8 max-w-6xl">
      <header class="flex items-baseline justify-between">
        <div>
          <h1 class="font-display text-3xl font-bold tracking-tight">Operations</h1>
          <p class="text-muted text-sm mt-1">{{ now | date: 'EEEE, d MMMM y' }}</p>
        </div>
        <div class="text-xs font-mono text-muted">refreshed {{ lastRefresh() | date: 'HH:mm:ss' }}</div>
      </header>

      <!-- KPI band -->
      <div class="grid grid-cols-1 sm:grid-cols-3 gap-px mt-8 bg-line border border-line rounded-lg overflow-hidden">
        <div class="bg-surface p-6 relative">
          <div class="absolute top-6 right-6 w-2.5 h-2.5 rounded-full bg-pulse"
               [class.animate-pulse-ring]="(presence()?.presentCount ?? 0) > 0"></div>
          <div class="text-xs uppercase tracking-widest text-muted font-medium">On site now</div>
          <div class="num text-5xl font-medium mt-3 text-pulse">{{ presence()?.presentCount ?? '—' }}</div>
          <div class="text-xs text-muted mt-2 font-mono">of {{ presence()?.activeEmployees ?? '—' }} active employees</div>
        </div>
        <div class="bg-surface p-6">
          <div class="text-xs uppercase tracking-widest text-muted font-medium">Presence rate</div>
          <div class="num text-5xl font-medium mt-3">{{ presenceRate() }}<span class="text-2xl text-muted">%</span></div>
          <div class="mt-3 h-1 bg-raised rounded-full overflow-hidden">
            <div class="h-full bg-pulse transition-all duration-700" [style.width.%]="presenceRate()"></div>
          </div>
        </div>
        <div class="bg-surface p-6">
          <div class="text-xs uppercase tracking-widest text-muted font-medium">Punches today</div>
          <div class="num text-5xl font-medium mt-3">{{ punchesToday() }}</div>
          <div class="text-xs text-muted mt-2 font-mono">across all sites &amp; devices</div>
        </div>
      </div>

      <div class="grid lg:grid-cols-[1fr_340px] gap-6 mt-6 items-start">
        <!-- who's in -->
        <section class="border border-line rounded-lg bg-surface">
          <h2 class="px-5 py-3.5 border-b border-line font-display font-bold text-sm tracking-wide">
            Currently clocked in
          </h2>
          <div class="divide-y divide-line/60 max-h-[420px] overflow-y-auto">
            @for (person of presence()?.present ?? []; track person.employeeId) {
              <div class="px-5 py-3 flex items-center gap-4 hover:bg-raised/40 transition-colors">
                <div class="w-8 h-8 rounded-full bg-raised border border-line grid place-items-center
                            font-display font-bold text-xs text-pulse">
                  {{ initials(person.employeeName) }}
                </div>
                <div class="min-w-0 flex-1">
                  <div class="text-sm truncate">{{ person.employeeName }}</div>
                  <div class="text-xs text-muted truncate">{{ person.jobTitle ?? '—' }}</div>
                </div>
                <div class="num text-xs text-muted">in {{ person.since | date: 'HH:mm' }}</div>
              </div>
            } @empty {
              <div class="px-5 py-10 text-center text-muted text-sm">Nobody is clocked in right now.</div>
            }
          </div>
        </section>

        <!-- the pulse column: live punch feed -->
        <section class="border border-line rounded-lg bg-surface">
          <h2 class="px-5 py-3.5 border-b border-line font-display font-bold text-sm tracking-wide
                     flex items-center justify-between">
            Live feed
            <span class="inline-block w-1.5 h-1.5 rounded-full bg-pulse animate-pulse-ring"></span>
          </h2>
          <div class="divide-y divide-line/60 max-h-[420px] overflow-y-auto">
            @for (entry of feed(); track entry.key) {
              <div class="px-5 py-2.5 flex items-center gap-3 text-sm"
                   [class.animate-ticker-in]="entry.fresh">
                <span class="num text-xs text-muted w-14">{{ entry.time | date: 'HH:mm:ss' }}</span>
                <span class="font-mono text-[10px] px-1.5 py-0.5 rounded border"
                      [class]="entry.direction === 'In'
                        ? 'text-pulse border-pulse/40 bg-pulse/10'
                        : 'text-coral border-coral/40 bg-coral/10'">
                  {{ entry.direction === 'In' ? 'IN' : 'OUT' }}
                </span>
                <span class="truncate flex-1">{{ entry.name }}</span>
                <span class="num text-[10px] text-muted">{{ entry.code }}</span>
              </div>
            } @empty {
              <div class="px-5 py-10 text-center text-muted text-sm">Waiting for punches…</div>
            }
          </div>
        </section>
      </div>
    </div>
  `,
})
export class DashboardComponent implements OnInit, OnDestroy {
  private readonly api = inject(WorkforceApi);
  private readonly realtime = inject(RealtimeService);
  private pollHandle: ReturnType<typeof setInterval> | null = null;

  readonly now = new Date();
  readonly presence = signal<LivePresence | null>(null);
  readonly feed = signal<FeedEntry[]>([]);
  readonly punchesToday = signal(0);
  readonly lastRefresh = signal(new Date());

  constructor() {
    // Live path: a punch arrives over SignalR → prepend to feed, refresh KPIs.
    effect(() => {
      const event = this.realtime.lastPunch();
      if (event) this.onLivePunch(event);
    });
  }

  ngOnInit(): void {
    this.reload();
    this.pollHandle = setInterval(() => this.reload(), 30_000); // fallback while realtime is down
  }

  ngOnDestroy(): void {
    if (this.pollHandle) clearInterval(this.pollHandle);
  }

  presenceRate(): number {
    const snapshot = this.presence();
    if (!snapshot || snapshot.activeEmployees === 0) return 0;
    return Math.round((snapshot.presentCount / snapshot.activeEmployees) * 100);
  }

  initials(name: string): string {
    return name.split(' ').map(part => part[0]).slice(0, 2).join('').toUpperCase();
  }

  private reload(): void {
    this.api.livePresence().subscribe(p => {
      this.presence.set(p);
      this.lastRefresh.set(new Date());
    });
    this.api.recentPunches(30).subscribe(punches => {
      const today = new Date().toDateString();
      this.punchesToday.set(punches.filter(p => new Date(p.timestamp).toDateString() === today).length);
      this.feed.set(punches.map(p => this.toEntry(p)));
    });
  }

  private onLivePunch(event: PunchEvent): void {
    this.feed.update(entries => [
      {
        key: event.punchId,
        name: event.employeeName,
        code: event.employeeCode,
        time: event.timestamp,
        direction: event.direction,
        fresh: true,
      },
      ...entries.slice(0, 29),
    ]);
    this.punchesToday.update(n => n + 1);
    this.api.livePresence().subscribe(p => this.presence.set(p));
  }

  private toEntry(punch: PunchRow): FeedEntry {
    return {
      key: punch.id,
      name: punch.employeeCode, // recent-punch endpoint is code-only; live events carry names
      code: punch.employeeCode,
      time: punch.timestamp,
      direction: punch.direction === 0 ? 'In' : 'Out',
      fresh: false,
    };
  }
}
