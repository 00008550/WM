import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject, signal, computed, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Subject, auditTime } from 'rxjs';
import { WorkforceApi, LivePresence, PunchRow } from '../../core/api/workforce.api';
import { RealtimeService, PunchEvent } from '../../core/realtime/realtime.service';
import { IconComponent } from '../../core/ui/icon.component';
import { PunchTimeComponent, ZoneLabelMode, mixesZones } from '../../shared/punch-time.pipe';

interface FeedEntry {
  key: string;
  name: string;
  code: string;
  time: string;
  /** The clock `time` is shown on — the punch's own zone, not the viewer's (022 P2). */
  zone: string | null;
  direction: 'In' | 'Out';
  fresh: boolean;
}

@Component({
  selector: 'wm-dashboard',
  imports: [DatePipe, FormsModule, IconComponent, PunchTimeComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="p-8 max-w-6xl">
      <header class="flex items-baseline justify-between rise" style="--i: 0">
        <div>
          <h1 class="font-display text-2xl font-bold tracking-tight">Operations</h1>
          <p class="text-muted text-sm mt-0.5">{{ now | date: 'EEEE, d MMMM y' }}</p>
        </div>
        <div class="text-[11px] font-mono text-muted" aria-live="polite">
          refreshed {{ lastRefresh() | date: 'HH:mm:ss' }}
        </div>
      </header>

      <!-- KPI band: divided, not boxed -->
      <div class="grid grid-cols-1 sm:grid-cols-3 mt-8 border-y border-line divide-y sm:divide-y-0 sm:divide-x divide-line rise" style="--i: 1">
        <div class="py-6 sm:pr-8 relative">
          <div class="absolute top-6 right-2 w-2 h-2 rounded-full bg-pulse"
               [class.animate-pulse-ring]="(presence()?.presentCount ?? 0) > 0"></div>
          <div class="text-[11px] uppercase tracking-[0.18em] text-muted font-medium">On site now</div>
          @if (loading()) {
            <div class="skeleton h-12 w-24 mt-3"></div>
          } @else {
            <div class="num text-5xl font-medium mt-2 text-pulse">{{ shownPresent() }}</div>
            <div class="text-[11px] text-muted mt-1.5 font-mono">of {{ presence()?.activeEmployees }} active employees</div>
          }
        </div>
        <div class="py-6 sm:px-8">
          <div class="text-[11px] uppercase tracking-[0.18em] text-muted font-medium">Presence rate</div>
          @if (loading()) {
            <div class="skeleton h-12 w-20 mt-3"></div>
          } @else {
            <div class="num text-5xl font-medium mt-2">{{ presenceRate() }}<span class="text-2xl text-muted">%</span></div>
            <div class="mt-2.5 h-px bg-line relative overflow-visible">
              <div class="absolute inset-y-[-1px] left-0 bg-pulse transition-all duration-700 ease-spring" [style.width.%]="presenceRate()"></div>
            </div>
          }
        </div>
        <div class="py-6 sm:pl-8">
          <div class="text-[11px] uppercase tracking-[0.18em] text-muted font-medium">Punches today</div>
          @if (loading()) {
            <div class="skeleton h-12 w-16 mt-3"></div>
          } @else {
            <div class="num text-5xl font-medium mt-2">{{ shownPunches() }}</div>
            <div class="text-[11px] text-muted mt-1.5 font-mono">all sites, all devices</div>
          }
        </div>
      </div>

      <div class="grid lg:grid-cols-[1fr_360px] gap-10 mt-10 items-start">
        <!-- who's in -->
        <section class="rise" style="--i: 2">
          <div class="flex items-baseline justify-between border-b border-line pb-3">
            <h2 class="font-display font-bold text-sm tracking-wide">Currently clocked in</h2>
            <span class="num text-[11px] text-muted">{{ presence()?.presentCount ?? 0 }} people</span>
          </div>
          <div class="divide-y divide-line/50 max-h-[430px] overflow-y-auto">
            @if (loading()) {
              @for (i of [0, 1, 2, 3, 4]; track i) {
                <div class="py-3 flex items-center gap-4">
                  <div class="skeleton w-8 h-8 rounded-full"></div>
                  <div class="flex-1 space-y-1.5">
                    <div class="skeleton h-3.5 w-40"></div>
                    <div class="skeleton h-3 w-24"></div>
                  </div>
                </div>
              }
            } @else {
              @for (person of presence()?.present ?? []; track person.employeeId) {
                <div class="py-2.5 pr-2 flex items-center gap-4 group">
                  <div class="w-8 h-8 rounded-full grid place-items-center font-display font-bold text-[11px] shrink-0
                              border transition-colors duration-150"
                       [style.border-color]="hue(person.employeeName, 0.45)"
                       [style.color]="hue(person.employeeName, 1)">
                    {{ initials(person.employeeName) }}
                  </div>
                  <div class="min-w-0 flex-1">
                    <div class="text-sm truncate">{{ person.employeeName }}</div>
                    <div class="text-xs text-muted truncate">{{ person.jobTitle ?? '—' }}</div>
                  </div>
                  <div class="num text-[11px] text-muted">in <wm-punch-time [value]="person.since" [zone]="person.sinceLocalZone" [label]="presenceLabels()" /></div>
                </div>
              } @empty {
                <div class="py-12 text-center">
                  <p class="text-muted text-sm">Nobody is clocked in right now.</p>
                  <p class="text-muted/60 text-xs mt-1">Punches appear here the moment they happen.</p>
                </div>
              }
            }
          </div>
        </section>

        <!-- pulse column -->
        <div class="space-y-8 rise" style="--i: 3">
          <!-- quick punch -->
          <section class="border border-line rounded-lg bg-surface p-4">
            <h2 class="font-display font-bold text-sm tracking-wide">Quick punch</h2>
            <form class="mt-3 flex gap-2" (submit)="$event.preventDefault()">
              <label class="sr-only" for="punch-code">Employee code</label>
              <input id="punch-code" name="employeeCode" [(ngModel)]="punchCode" spellcheck="false"
                     placeholder="E1004" autocomplete="off"
                     class="num min-w-0 flex-1 bg-raised border border-line rounded-md px-3 py-2 text-sm
                            placeholder:text-muted/40 transition-colors duration-150 focus:border-pulse/60" />
              <button type="submit" (click)="punch('In')" [disabled]="punchBusy() || !punchCode"
                      class="flex items-center gap-1.5 px-3 py-2 rounded-md bg-pulse text-ink font-display font-bold text-xs
                             transition-transform duration-100 active:scale-[0.97] disabled:opacity-40">
                <wm-icon name="arrow-in" [size]="14" /> IN
              </button>
              <button type="button" (click)="punch('Out')" [disabled]="punchBusy() || !punchCode"
                      class="flex items-center gap-1.5 px-3 py-2 rounded-md border border-coral/50 text-coral font-display font-bold text-xs
                             transition-all duration-100 hover:bg-coral/10 active:scale-[0.97] disabled:opacity-40">
                <wm-icon name="arrow-out" [size]="14" /> OUT
              </button>
            </form>
            @if (punchError()) {
              <p class="mt-2 text-coral text-xs" role="alert">{{ punchError() }}</p>
            }
          </section>

          <!-- live feed -->
          <section>
            <div class="flex items-center justify-between border-b border-line pb-3">
              <h2 class="font-display font-bold text-sm tracking-wide">Live feed</h2>
              <span class="inline-block w-1.5 h-1.5 rounded-full bg-pulse animate-pulse-ring"></span>
            </div>
            <div class="divide-y divide-line/50 max-h-[330px] overflow-y-auto" aria-live="polite">
              @if (loading()) {
                @for (i of [0, 1, 2, 3]; track i) {
                  <div class="py-2.5 flex items-center gap-3">
                    <div class="skeleton h-3 w-12"></div>
                    <div class="skeleton h-4 w-8"></div>
                    <div class="skeleton h-3 flex-1"></div>
                  </div>
                }
              } @else {
                @for (entry of feed(); track entry.key) {
                  <div class="py-2 flex items-center gap-2.5 text-sm" [class.animate-ticker-in]="entry.fresh">
                    <span class="num text-[10px] text-muted min-w-12 shrink-0 whitespace-nowrap"><wm-punch-time [value]="entry.time" [zone]="entry.zone" format="HH:mm:ss" [label]="feedLabels()" /></span>
                    <span class="font-mono text-[9px] px-1.5 py-0.5 rounded-sm border shrink-0"
                          [class]="entry.direction === 'In'
                            ? 'text-pulse border-pulse/40 bg-pulse/10'
                            : 'text-coral border-coral/40 bg-coral/10'">
                      {{ entry.direction === 'In' ? 'IN' : 'OUT' }}
                    </span>
                    <span class="truncate flex-1 min-w-0">{{ entry.name }}</span>
                    <span class="num text-[10px] text-muted/70 shrink-0">{{ entry.code }}</span>
                  </div>
                } @empty {
                  <div class="py-10 text-center text-muted text-sm">Waiting for punches…</div>
                }
              }
            </div>
          </section>
        </div>
      </div>
    </div>
  `,
})
export class DashboardComponent implements OnInit, OnDestroy {
  private readonly api = inject(WorkforceApi);
  private readonly realtime = inject(RealtimeService);
  private pollHandle: ReturnType<typeof setInterval> | null = null;

  readonly now = new Date();
  readonly loading = signal(true);
  readonly presence = signal<LivePresence | null>(null);
  readonly feed = signal<FeedEntry[]>([]);
  readonly punchesToday = signal(0);
  readonly lastRefresh = signal(new Date());

  // count-up display values so KPIs animate to their target
  readonly shownPresent = signal(0);
  readonly shownPunches = signal(0);

  /** A list that mixes zones labels every row, so no row reads as the viewer's clock by omission. */
  readonly feedLabels = computed<ZoneLabelMode>(() => mixesZones(this.feed().map(e => e.zone)) ? 'always' : 'auto');
  readonly presenceLabels = computed<ZoneLabelMode>(() =>
    mixesZones((this.presence()?.present ?? []).map(p => p.sinceLocalZone)) ? 'always' : 'auto');

  punchCode = '';
  readonly punchBusy = signal(false);
  readonly punchError = signal<string | null>(null);

  /** Coalesces presence refreshes triggered by incoming punches. */
  private readonly presenceRefresh = new Subject<void>();

  constructor() {
    // Subscribe to the punch stream. Note this is NOT an `effect`: the handler
    // writes signals it also reads, so inside an effect it would re-trigger
    // itself indefinitely and flood the API with refresh calls.
    this.realtime.punches$
      .pipe(takeUntilDestroyed())
      .subscribe(event => this.onLivePunch(event));

    // A busy site can produce many punches per second. Collapse the resulting
    // presence refreshes so the dashboard issues at most one request per window
    // instead of one per punch.
    this.presenceRefresh
      .pipe(auditTime(1500), takeUntilDestroyed())
      .subscribe(() => this.refreshPresence());

    // The server re-grouped this socket: what we may see changed mid-session. Drop the feed
    // and reload rather than keep rows we may no longer be entitled to — the scope could have
    // narrowed as easily as widened, and the client cannot tell which from here.
    this.realtime.scopeChanged$
      .pipe(takeUntilDestroyed())
      .subscribe(() => {
        this.feed.set([]);
        this.reload();
      });
  }

  ngOnInit(): void {
    this.reload();
    this.pollHandle = setInterval(() => this.reload(), 30_000);
  }

  ngOnDestroy(): void {
    if (this.pollHandle) clearInterval(this.pollHandle);
    for (const frame of this.animations.values()) cancelAnimationFrame(frame);
    this.animations.clear();
  }

  presenceRate(): number {
    const snapshot = this.presence();
    if (!snapshot || snapshot.activeEmployees === 0) return 0;
    return Math.round((snapshot.presentCount / snapshot.activeEmployees) * 100);
  }

  initials(name: string): string {
    return name.split(' ').map(part => part[0]).slice(0, 2).join('').toUpperCase();
  }

  /** Deterministic per-person hue for avatars — colorful without being random. */
  hue(name: string, alpha: number): string {
    let hash = 0;
    for (const char of name) hash = (hash * 31 + char.charCodeAt(0)) | 0;
    return `hsl(${((hash % 360) + 360) % 360} 55% 62% / ${alpha})`;
  }

  punch(direction: 'In' | 'Out'): void {
    if (!this.punchCode || this.punchBusy()) return;
    this.punchBusy.set(true);
    this.punchError.set(null);
    this.api.recordPunch(this.punchCode.trim().toUpperCase(), direction).subscribe({
      next: () => {
        this.punchBusy.set(false);
        this.punchCode = '';
      },
      error: err => {
        this.punchBusy.set(false);
        this.punchError.set(err?.error?.detail ?? 'Punch failed — check the employee code.');
      },
    });
  }

  private reload(): void {
    this.api.livePresence().subscribe(p => {
      this.presence.set(p);
      this.lastRefresh.set(new Date());
      this.countUp(this.shownPresent, p.presentCount);
    });
    this.api.recentPunches(30).subscribe(punches => {
      const today = new Date().toDateString();
      const count = punches.filter(p => new Date(p.timestamp).toDateString() === today).length;
      this.feed.set(punches.map(p => this.toEntry(p)));
      this.loading.set(false);
      this.countUp(this.shownPunches, count);
      this.punchesToday.set(count);
    });
  }

  /** In-flight count-up animations, so a rapid second call replaces the first. */
  private readonly animations = new Map<unknown, number>();

  private countUp(target: typeof this.shownPresent, to: number): void {
    // Cancel any animation already running for this target; without this, rapid
    // updates leave several frame loops writing the same signal at once.
    const running = this.animations.get(target);
    if (running !== undefined) cancelAnimationFrame(running);

    const from = target();
    if (from === to) {
      this.animations.delete(target);
      return;
    }

    const start = performance.now();
    const duration = 600;
    const step = (t: number) => {
      const k = Math.min(1, (t - start) / duration);
      const eased = 1 - Math.pow(1 - k, 3);
      target.set(Math.round(from + (to - from) * eased));
      if (k < 1) this.animations.set(target, requestAnimationFrame(step));
      else this.animations.delete(target);
    };
    this.animations.set(target, requestAnimationFrame(step));
  }

  private onLivePunch(event: PunchEvent): void {
    // Ignore an event we already have — a reconnect can replay one, and a
    // duplicate would otherwise inflate today's count.
    if (this.feed().some(e => e.key === event.punchId)) return;

    this.feed.update(entries => [
      {
        key: event.punchId,
        name: event.employeeName,
        code: event.employeeCode,
        time: event.timestamp,
        zone: event.localZone ?? null,
        direction: event.direction,
        fresh: true,
      },
      ...entries.slice(0, 29),
    ]);

    const total = this.punchesToday() + 1;
    this.punchesToday.set(total);
    this.countUp(this.shownPunches, total);

    // Coalesced by auditTime in the constructor rather than fired per punch.
    this.presenceRefresh.next();
  }

  private refreshPresence(): void {
    this.api.livePresence().subscribe(p => {
      this.presence.set(p);
      this.countUp(this.shownPresent, p.presentCount);
    });
  }

  private toEntry(punch: PunchRow): FeedEntry {
    return {
      key: punch.id,
      name: punch.employeeName,
      code: punch.employeeCode,
      time: punch.timestamp,
      zone: punch.localZone,
      direction: punch.direction === 0 ? 'In' : 'Out',
      fresh: false,
    };
  }
}
