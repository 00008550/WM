import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { WorkforceApi, EmployeeRow, Paged, Site, EmployeeUpsert } from '../../core/api/workforce.api';
import { AuthService } from '../../core/auth/auth.service';
import { IconComponent } from '../../core/ui/icon.component';

@Component({
  selector: 'wm-employees',
  imports: [FormsModule, DatePipe, IconComponent],
  template: `
    <div class="p-8 max-w-6xl">
      <header class="flex items-end justify-between gap-4 flex-wrap rise" style="--i: 0">
        <div>
          <h1 class="font-display text-2xl font-bold tracking-tight">Employees</h1>
          <p class="text-muted text-sm mt-0.5">
            <span class="num">{{ data()?.total ?? '—' }}</span> people across all sites
          </p>
        </div>
        <div class="flex items-center gap-2">
          <div class="relative">
            <wm-icon name="search" [size]="14" class="absolute left-3 top-1/2 -translate-y-1/2 text-muted pointer-events-none" />
            <label class="sr-only" for="employee-search">Search employees</label>
            <input id="employee-search" type="search" name="search" placeholder="Search name or code…"
                   [ngModel]="search()" (ngModelChange)="onSearch($event)" spellcheck="false" autocomplete="off"
                   class="bg-raised border border-line rounded-md pl-9 pr-3.5 py-2 text-sm w-72
                          placeholder:text-muted/50 transition-colors duration-150 focus:border-pulse/60" />
          </div>
          @if (canManage()) {
            <button (click)="openCreate()"
                    class="flex items-center gap-2 px-3.5 py-2 rounded-md bg-pulse text-ink font-display font-bold text-sm
                           transition-transform duration-100 active:scale-[0.97] whitespace-nowrap">
              <wm-icon name="plus" [size]="15" /> New employee
            </button>
          }
        </div>
      </header>

      <div class="mt-6 rise" style="--i: 1">
        <table class="w-full text-sm">
          <thead class="sticky top-0 bg-ink z-10">
            <tr class="text-left text-[11px] uppercase tracking-[0.18em] text-muted border-b border-line">
              <th class="py-3 pr-4 font-medium">Code</th>
              <th class="py-3 pr-4 font-medium">Name</th>
              <th class="py-3 pr-4 font-medium">Role</th>
              <th class="py-3 pr-4 font-medium">Hired</th>
              <th class="py-3 pr-4 font-medium">Status</th>
              <th class="py-3 font-medium"></th>
            </tr>
          </thead>
          <tbody class="divide-y divide-line/50">
            @if (loading()) {
              @for (i of [0, 1, 2, 3, 4, 5, 6]; track i) {
                <tr>
                  <td class="py-3.5 pr-4"><div class="skeleton h-3 w-12"></div></td>
                  <td class="py-3.5 pr-4"><div class="skeleton h-3.5 w-44"></div></td>
                  <td class="py-3.5 pr-4"><div class="skeleton h-3 w-28"></div></td>
                  <td class="py-3.5 pr-4"><div class="skeleton h-3 w-16"></div></td>
                  <td class="py-3.5 pr-4"><div class="skeleton h-4 w-14"></div></td>
                  <td class="py-3.5"></td>
                </tr>
              }
            } @else {
              @for (employee of data()?.items ?? []; track employee.id) {
                <tr class="group hover:bg-raised/40 transition-colors duration-100">
                  <td class="py-3 pr-4 num text-xs text-muted">{{ employee.code }}</td>
                  <td class="py-3 pr-4">
                    <div class="flex items-center gap-3">
                      <span class="w-7 h-7 rounded-full grid place-items-center font-display font-bold text-[10px] border shrink-0"
                            [style.border-color]="hue(employee.firstName + employee.lastName, 0.45)"
                            [style.color]="hue(employee.firstName + employee.lastName, 1)">
                        {{ employee.firstName[0] }}{{ employee.lastName[0] }}
                      </span>
                      <span class="min-w-0">
                        {{ employee.firstName }} {{ employee.lastName }}
                        <span class="block text-xs text-muted truncate">{{ employee.email }}</span>
                      </span>
                    </div>
                  </td>
                  <td class="py-3 pr-4 text-muted">{{ employee.jobTitle ?? '—' }}</td>
                  <td class="py-3 pr-4 num text-xs text-muted">{{ employee.hireDate | date: 'MMM y' }}</td>
                  <td class="py-3 pr-4">
                    <span class="font-mono text-[9px] px-1.5 py-0.5 rounded-sm border"
                          [class]="statusClass(employee.status)">{{ statusLabel(employee.status) }}</span>
                  </td>
                  <td class="py-3 text-right">
                    @if (canManage()) {
                      <button (click)="openEdit(employee)"
                              class="text-xs text-muted hover:text-text transition-colors">Edit</button>
                    }
                  </td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="6" class="py-14 text-center">
                    @if (search()) {
                      <p class="text-muted">No employees match “{{ search() }}”.</p>
                      <p class="text-muted/60 text-xs mt-1">Try a shorter name or an employee code like E1004.</p>
                    } @else {
                      <p class="text-muted">No employees yet.</p>
                      <p class="text-muted/60 text-xs mt-1">Add your first employee to start recording attendance.</p>
                    }
                  </td>
                </tr>
              }
            }
          </tbody>
        </table>

        @if ((data()?.totalPages ?? 0) > 1) {
          <div class="flex items-center justify-between py-3 border-t border-line text-xs text-muted">
            <span class="num">page {{ page() }} / {{ data()?.totalPages }}</span>
            <div class="flex gap-1.5">
              <button (click)="go(page() - 1)" [disabled]="page() <= 1" aria-label="Previous page"
                      class="p-1.5 rounded border border-line hover:border-pulse/50 hover:text-text
                             transition-colors duration-150 disabled:opacity-40 disabled:pointer-events-none">
                <wm-icon name="chevron-left" [size]="13" />
              </button>
              <button (click)="go(page() + 1)" [disabled]="page() >= (data()?.totalPages ?? 1)" aria-label="Next page"
                      class="p-1.5 rounded border border-line hover:border-pulse/50 hover:text-text
                             transition-colors duration-150 disabled:opacity-40 disabled:pointer-events-none">
                <wm-icon name="chevron-right" [size]="13" />
              </button>
            </div>
          </div>
        }
      </div>
    </div>

    @if (editing()) {
      <div class="fixed inset-0 z-40 bg-ink/70 backdrop-blur-sm" (click)="close()"></div>
      <aside class="fixed inset-y-0 right-0 z-50 w-full max-w-md bg-surface border-l border-line
                    overflow-y-auto p-6 flex flex-col gap-4" role="dialog" aria-modal="true">
        <div class="flex items-center justify-between">
          <h2 class="font-display text-lg font-bold">{{ isNew() ? 'New employee' : 'Edit employee' }}</h2>
          <button (click)="close()" aria-label="Close" class="text-muted hover:text-text text-lg">✕</button>
        </div>

        <label class="block">
          <span class="text-xs uppercase tracking-widest text-muted">Employee code</span>
          <input [(ngModel)]="form.code" spellcheck="false" autocomplete="off" placeholder="E1042"
                 class="mt-1.5 w-full bg-raised border border-line rounded-md px-3 py-2 text-sm num focus:border-pulse/60" />
        </label>

        <div class="grid grid-cols-2 gap-3">
          <label class="block">
            <span class="text-xs uppercase tracking-widest text-muted">First name</span>
            <input [(ngModel)]="form.firstName"
                   class="mt-1.5 w-full bg-raised border border-line rounded-md px-3 py-2 text-sm focus:border-pulse/60" />
          </label>
          <label class="block">
            <span class="text-xs uppercase tracking-widest text-muted">Last name</span>
            <input [(ngModel)]="form.lastName"
                   class="mt-1.5 w-full bg-raised border border-line rounded-md px-3 py-2 text-sm focus:border-pulse/60" />
          </label>
        </div>

        <label class="block">
          <span class="text-xs uppercase tracking-widest text-muted">Job title</span>
          <input [(ngModel)]="form.jobTitle"
                 class="mt-1.5 w-full bg-raised border border-line rounded-md px-3 py-2 text-sm focus:border-pulse/60" />
        </label>

        <label class="block">
          <span class="text-xs uppercase tracking-widest text-muted">Email</span>
          <input [(ngModel)]="form.email" type="email" autocomplete="off" spellcheck="false"
                 class="mt-1.5 w-full bg-raised border border-line rounded-md px-3 py-2 text-sm focus:border-pulse/60" />
        </label>

        <label class="block">
          <span class="text-xs uppercase tracking-widest text-muted">Phone</span>
          <input [(ngModel)]="form.phone" type="tel" autocomplete="off"
                 class="mt-1.5 w-full bg-raised border border-line rounded-md px-3 py-2 text-sm focus:border-pulse/60" />
        </label>

        <label class="block">
          <span class="text-xs uppercase tracking-widest text-muted">Site</span>
          <select [(ngModel)]="form.siteId"
                  class="mt-1.5 w-full bg-raised border border-line rounded-md px-3 py-2 text-sm focus:border-pulse/60">
            <option [ngValue]="''">— select a site —</option>
            @for (s of sites(); track s.id) {
              <option [ngValue]="s.id">{{ s.name }}</option>
            }
          </select>
        </label>

        <label class="block">
          <span class="text-xs uppercase tracking-widest text-muted">Hire date</span>
          <input [(ngModel)]="form.hireDate" type="date"
                 class="mt-1.5 w-full bg-raised border border-line rounded-md px-3 py-2 text-sm num focus:border-pulse/60" />
        </label>

        @if (!isNew()) {
          <label class="block">
            <span class="text-xs uppercase tracking-widest text-muted">Status</span>
            <select [(ngModel)]="form.status"
                    class="mt-1.5 w-full bg-raised border border-line rounded-md px-3 py-2 text-sm focus:border-pulse/60">
              <option [ngValue]="0">Active</option>
              <option [ngValue]="1">On leave</option>
              <option [ngValue]="2">Terminated</option>
            </select>
          </label>
        }

        @if (error()) { <p class="text-coral text-xs" role="alert">{{ error() }}</p> }

        <div class="mt-auto pt-4">
          <button (click)="save()" [disabled]="busy()"
                  class="w-full bg-pulse text-ink font-display font-bold rounded-md py-2.5
                         transition-transform active:scale-[0.99] disabled:opacity-40">
            {{ busy() ? 'Saving…' : 'Save' }}
          </button>
        </div>
      </aside>
    }
  `,
})
export class EmployeesComponent implements OnInit {
  private readonly api = inject(WorkforceApi);
  private readonly auth = inject(AuthService);
  private readonly searchInput = new Subject<string>();

  readonly data = signal<Paged<EmployeeRow> | null>(null);
  readonly loading = signal(true);
  readonly page = signal(1);
  readonly search = signal('');
  readonly sites = signal<Site[]>([]);

  readonly editing = signal(false);
  readonly isNew = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  private editingId: string | null = null;

  form = this.blank();

  canManage(): boolean {
    return this.auth.hasPermission('employees.manage');
  }

  constructor() {
    this.searchInput
      .pipe(debounceTime(250), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe(term => {
        this.search.set(term);
        this.page.set(1);
        this.load();
      });
  }

  ngOnInit(): void {
    this.load();
    if (this.canManage()) this.api.sites().subscribe(s => this.sites.set(s));
  }

  onSearch(term: string): void {
    this.searchInput.next(term);
  }

  openCreate(): void {
    this.form = this.blank();
    this.editingId = null;
    this.isNew.set(true);
    this.error.set(null);
    this.editing.set(true);
  }

  openEdit(e: EmployeeRow): void {
    this.editingId = e.id;
    this.form = {
      code: e.code, firstName: e.firstName, lastName: e.lastName,
      email: e.email ?? '', phone: '', jobTitle: e.jobTitle ?? '',
      siteId: e.siteId, hireDate: (e.hireDate ?? '').slice(0, 10), status: e.status,
    };
    this.isNew.set(false);
    this.error.set(null);
    this.editing.set(true);
  }

  close(): void {
    this.editing.set(false);
  }

  save(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);

    const payload: EmployeeUpsert = {
      code: this.form.code.trim(),
      firstName: this.form.firstName.trim(),
      lastName: this.form.lastName.trim(),
      email: this.form.email.trim() || null,
      phone: this.form.phone.trim() || null,
      jobTitle: this.form.jobTitle.trim() || null,
      siteId: this.form.siteId,
      departmentId: null,
      hireDate: this.form.hireDate || null,
      status: this.form.status,
    };

    const done = {
      next: () => { this.busy.set(false); this.editing.set(false); this.load(); },
      error: (e: any) => {
        this.busy.set(false);
        // ProblemDetails puts the message in `detail`; fall back for anything else.
        this.error.set(e?.error?.detail ?? e?.error?.title ?? 'Save failed.');
      },
    };

    if (this.isNew()) this.api.createEmployee(payload).subscribe(done);
    else if (this.editingId) this.api.updateEmployee(this.editingId, payload).subscribe(done);
  }

  private blank() {
    return {
      code: '', firstName: '', lastName: '', email: '', phone: '', jobTitle: '',
      siteId: '', hireDate: '', status: 0,
    };
  }

  go(page: number): void {
    this.page.set(page);
    this.load();
  }

  hue(name: string, alpha: number): string {
    let hash = 0;
    for (const char of name) hash = (hash * 31 + char.charCodeAt(0)) | 0;
    return `hsl(${((hash % 360) + 360) % 360} 55% 62% / ${alpha})`;
  }

  statusLabel(status: number): string {
    return ['ACTIVE', 'ON LEAVE', 'TERMINATED'][status] ?? '?';
  }

  statusClass(status: number): string {
    return [
      'text-pulse border-pulse/40 bg-pulse/10',
      'text-amber border-amber/40 bg-amber/10',
      'text-muted border-line bg-raised',
    ][status] ?? '';
  }

  private load(): void {
    this.api.employees(this.search(), this.page()).subscribe(result => {
      this.data.set(result);
      this.loading.set(false);
    });
  }
}
