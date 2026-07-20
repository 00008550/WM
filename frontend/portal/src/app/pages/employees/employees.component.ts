import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { WorkforceApi, EmployeeRow, Paged } from '../../core/api/workforce.api';
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
        <div class="relative">
          <wm-icon name="search" [size]="14" class="absolute left-3 top-1/2 -translate-y-1/2 text-muted pointer-events-none" />
          <label class="sr-only" for="employee-search">Search employees</label>
          <input id="employee-search" type="search" name="search" placeholder="Search name or code…"
                 [ngModel]="search()" (ngModelChange)="onSearch($event)" spellcheck="false" autocomplete="off"
                 class="bg-raised border border-line rounded-md pl-9 pr-3.5 py-2 text-sm w-72
                        placeholder:text-muted/50 transition-colors duration-150 focus:border-pulse/60" />
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
              <th class="py-3 font-medium">Status</th>
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
                  <td class="py-3.5"><div class="skeleton h-4 w-14"></div></td>
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
                  <td class="py-3">
                    <span class="font-mono text-[9px] px-1.5 py-0.5 rounded-sm border"
                          [class]="statusClass(employee.status)">{{ statusLabel(employee.status) }}</span>
                  </td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="5" class="py-14 text-center">
                    <p class="text-muted">No employees match “{{ search() }}”.</p>
                    <p class="text-muted/60 text-xs mt-1">Try a shorter name or an employee code like E1004.</p>
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
  `,
})
export class EmployeesComponent implements OnInit {
  private readonly api = inject(WorkforceApi);
  private readonly searchInput = new Subject<string>();

  readonly data = signal<Paged<EmployeeRow> | null>(null);
  readonly loading = signal(true);
  readonly page = signal(1);
  readonly search = signal('');

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
  }

  onSearch(term: string): void {
    this.searchInput.next(term);
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
