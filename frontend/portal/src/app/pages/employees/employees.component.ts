import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { WorkforceApi, EmployeeRow, Paged } from '../../core/api/workforce.api';

@Component({
  selector: 'wm-employees',
  imports: [FormsModule, DatePipe],
  template: `
    <div class="p-8 max-w-6xl">
      <header class="flex items-end justify-between gap-4 flex-wrap">
        <div>
          <h1 class="font-display text-3xl font-bold tracking-tight">Employees</h1>
          <p class="text-muted text-sm mt-1">
            <span class="num">{{ data()?.total ?? '—' }}</span> people across all sites
          </p>
        </div>
        <input type="search" placeholder="Search name or code…" [ngModel]="search()"
               (ngModelChange)="onSearch($event)"
               class="bg-raised border border-line rounded-md px-3.5 py-2 text-sm w-72
                      placeholder:text-muted/50 transition-colors focus:border-pulse/60" />
      </header>

      <div class="mt-6 border border-line rounded-lg overflow-hidden bg-surface">
        <table class="w-full text-sm">
          <thead>
            <tr class="text-left text-xs uppercase tracking-widest text-muted border-b border-line">
              <th class="px-5 py-3 font-medium">Code</th>
              <th class="px-5 py-3 font-medium">Name</th>
              <th class="px-5 py-3 font-medium">Role</th>
              <th class="px-5 py-3 font-medium">Hired</th>
              <th class="px-5 py-3 font-medium">Status</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-line/60">
            @for (employee of data()?.items ?? []; track employee.id) {
              <tr class="hover:bg-raised/40 transition-colors">
                <td class="px-5 py-3 num text-xs text-muted">{{ employee.code }}</td>
                <td class="px-5 py-3">
                  {{ employee.firstName }} {{ employee.lastName }}
                  <div class="text-xs text-muted">{{ employee.email }}</div>
                </td>
                <td class="px-5 py-3 text-muted">{{ employee.jobTitle ?? '—' }}</td>
                <td class="px-5 py-3 num text-xs text-muted">{{ employee.hireDate | date: 'MMM y' }}</td>
                <td class="px-5 py-3">
                  <span class="font-mono text-[10px] px-1.5 py-0.5 rounded border"
                        [class]="statusClass(employee.status)">{{ statusLabel(employee.status) }}</span>
                </td>
              </tr>
            } @empty {
              <tr><td colspan="5" class="px-5 py-12 text-center text-muted">No employees match.</td></tr>
            }
          </tbody>
        </table>

        @if ((data()?.totalPages ?? 0) > 1) {
          <div class="flex items-center justify-between px-5 py-3 border-t border-line text-xs text-muted">
            <span class="num">page {{ page() }} / {{ data()?.totalPages }}</span>
            <div class="flex gap-2">
              <button (click)="go(page() - 1)" [disabled]="page() <= 1"
                      class="px-2.5 py-1 rounded border border-line hover:border-pulse/50 hover:text-text
                             transition-colors disabled:opacity-40 disabled:pointer-events-none">‹ prev</button>
              <button (click)="go(page() + 1)" [disabled]="page() >= (data()?.totalPages ?? 1)"
                      class="px-2.5 py-1 rounded border border-line hover:border-pulse/50 hover:text-text
                             transition-colors disabled:opacity-40 disabled:pointer-events-none">next ›</button>
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
    this.api.employees(this.search(), this.page()).subscribe(result => this.data.set(result));
  }
}
