import { Component, OnInit, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import {
  SecurityGroupsApi, SecurityGroup, SecurityGroupUpsert, DataScopeKind, SCOPE_LABELS,
} from '../../core/api/security-groups.api';
import { WorkforceApi, Site } from '../../core/api/workforce.api';
import { IconComponent } from '../../core/ui/icon.component';

@Component({
  selector: 'wm-security-groups',
  imports: [FormsModule, IconComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="p-8 max-w-5xl">
      <header class="flex items-end justify-between gap-4 flex-wrap rise" style="--i: 0">
        <div>
          <h1 class="font-display text-2xl font-bold tracking-tight">Security groups</h1>
          <p class="text-muted text-sm mt-0.5 max-w-xl">
            Groups decide <em>which employees</em> a user can see. What they can <em>do</em> is set by
            their roles — the two are separate on purpose.
          </p>
        </div>
        <button (click)="openCreate()"
                class="flex items-center gap-2 px-3.5 py-2 rounded-md bg-pulse text-ink font-display font-bold text-sm
                       transition-transform duration-100 active:scale-[0.97]">
          <wm-icon name="plus" [size]="15" /> New group
        </button>
      </header>

      <div class="mt-6 rise" style="--i: 1">
        <table class="w-full text-sm">
          <thead>
            <tr class="text-left text-[11px] uppercase tracking-[0.18em] text-muted border-b border-line">
              <th class="py-3 pr-4 font-medium">Group</th>
              <th class="py-3 pr-4 font-medium">Visibility</th>
              <th class="py-3 pr-4 font-medium">Members</th>
              <th class="py-3 font-medium"></th>
            </tr>
          </thead>
          <tbody class="divide-y divide-line/50">
            @if (loading()) {
              @for (i of [0,1,2]; track i) {
                <tr><td class="py-3.5" colspan="4"><div class="skeleton h-4 w-full"></div></td></tr>
              }
            } @else {
              @for (g of groups(); track g.id) {
                <tr class="hover:bg-raised/40 transition-colors duration-100">
                  <td class="py-3 pr-4">
                    <div class="flex items-center gap-2">
                      {{ g.name }}
                      @if (g.isSystem) {
                        <span class="font-mono text-[9px] px-1.5 py-0.5 rounded-sm border border-line bg-raised text-muted">SYSTEM</span>
                      }
                    </div>
                    <div class="text-xs text-muted">{{ g.description }}</div>
                  </td>
                  <td class="py-3 pr-4">
                    <span class="font-mono text-[9px] px-1.5 py-0.5 rounded-sm border"
                          [class]="scopeClass(g.scopeKind)">{{ scopeLabel(g.scopeKind) }}</span>
                    @if (g.scopeKind === DataScopeKind.Sites) {
                      <span class="text-xs text-muted ml-2">{{ g.siteIds.length }} site(s){{ g.includeChildSites ? ' + children' : '' }}</span>
                    }
                  </td>
                  <td class="py-3 pr-4 num text-xs text-muted">{{ g.memberCount }}</td>
                  <td class="py-3 text-right whitespace-nowrap">
                    <button (click)="openEdit(g)" class="text-xs text-muted hover:text-text transition-colors">Edit</button>
                    @if (!g.isSystem) {
                      <button (click)="remove(g)" class="ml-3 text-xs text-muted hover:text-coral transition-colors">Delete</button>
                    }
                  </td>
                </tr>
              } @empty {
                <tr><td colspan="4" class="py-14 text-center text-muted">No security groups yet.</td></tr>
              }
            }
          </tbody>
        </table>
      </div>
    </div>

    @if (editing()) {
      <div class="fixed inset-0 z-40 bg-ink/70 backdrop-blur-sm" (click)="close()"></div>
      <aside class="fixed inset-y-0 right-0 z-50 w-full max-w-md bg-surface border-l border-line
                    overflow-y-auto p-6 flex flex-col gap-4" role="dialog" aria-modal="true">
        <div class="flex items-center justify-between">
          <h2 class="font-display text-lg font-bold">{{ isNew() ? 'New group' : 'Edit group' }}</h2>
          <button (click)="close()" aria-label="Close" class="text-muted hover:text-text text-lg">✕</button>
        </div>

        @if (form.isSystem) {
          <p class="text-xs text-amber border border-amber/40 bg-amber/10 rounded-md px-3 py-2">
            System group — only the description can be changed.
          </p>
        }

        <label class="block">
          <span class="text-xs uppercase tracking-widest text-muted">Name</span>
          <input [(ngModel)]="form.name" [disabled]="form.isSystem"
                 class="mt-1.5 w-full bg-raised border border-line rounded-md px-3 py-2 text-sm focus:border-pulse/60 disabled:opacity-50" />
        </label>

        <label class="block">
          <span class="text-xs uppercase tracking-widest text-muted">Description</span>
          <input [(ngModel)]="form.description"
                 class="mt-1.5 w-full bg-raised border border-line rounded-md px-3 py-2 text-sm focus:border-pulse/60" />
        </label>

        <label class="block">
          <span class="text-xs uppercase tracking-widest text-muted">Employees this group can see</span>
          <select [(ngModel)]="form.scopeKind" [disabled]="form.isSystem"
                  class="mt-1.5 w-full bg-raised border border-line rounded-md px-3 py-2 text-sm focus:border-pulse/60 disabled:opacity-50">
            <option [ngValue]="DataScopeKind.None">No employee data</option>
            <option [ngValue]="DataScopeKind.Self">Own record only</option>
            <option [ngValue]="DataScopeKind.Sites">Chosen sites</option>
            <option [ngValue]="DataScopeKind.All">All employees</option>
          </select>
        </label>

        @if (form.scopeKind === DataScopeKind.Sites) {
          <div>
            <span class="text-xs uppercase tracking-widest text-muted">Sites</span>
            <div class="mt-2 space-y-1.5">
              @for (s of sites(); track s.id) {
                <label class="flex items-center gap-2.5 text-sm cursor-pointer">
                  <input type="checkbox" [checked]="form.siteIds.includes(s.id)" (change)="toggleSite(s.id)"
                         class="accent-pulse w-4 h-4" />
                  {{ s.name }}
                </label>
              }
            </div>
            <label class="flex items-center gap-2.5 text-sm cursor-pointer mt-3">
              <input type="checkbox" [(ngModel)]="form.includeChildSites" class="accent-pulse w-4 h-4" />
              Include child sites
            </label>
          </div>
        }

        @if (error()) { <p class="text-coral text-xs" role="alert">{{ error() }}</p> }

        <div class="mt-auto pt-4">
          <button (click)="save()" [disabled]="busy()"
                  class="w-full bg-pulse text-ink font-display font-bold rounded-md py-2.5 transition-transform active:scale-[0.99] disabled:opacity-40">
            {{ busy() ? 'Saving…' : 'Save' }}
          </button>
        </div>
      </aside>
    }
  `,
})
export class SecurityGroupsComponent implements OnInit {
  private readonly api = inject(SecurityGroupsApi);
  private readonly workforce = inject(WorkforceApi);

  readonly DataScopeKind = DataScopeKind;
  readonly loading = signal(true);
  readonly groups = signal<SecurityGroup[]>([]);
  readonly sites = signal<Site[]>([]);
  readonly editing = signal(false);
  readonly isNew = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  private editingId: string | null = null;

  form = this.blank();

  ngOnInit(): void {
    forkJoin({ groups: this.api.list(), sites: this.workforce.sites() }).subscribe(({ groups, sites }) => {
      this.groups.set(groups);
      this.sites.set(sites);
      this.loading.set(false);
    });
  }

  scopeLabel(kind: DataScopeKind): string {
    return SCOPE_LABELS[kind] ?? 'Unknown';
  }

  scopeClass(kind: DataScopeKind): string {
    switch (kind) {
      case DataScopeKind.All: return 'text-pulse border-pulse/40 bg-pulse/10';
      case DataScopeKind.Sites:
      case DataScopeKind.Departments: return 'text-amber border-amber/40 bg-amber/10';
      default: return 'text-muted border-line bg-raised';
    }
  }

  openCreate(): void {
    this.form = this.blank();
    this.isNew.set(true);
    this.error.set(null);
    this.editing.set(true);
  }

  openEdit(g: SecurityGroup): void {
    this.editingId = g.id;
    this.form = {
      name: g.name, description: g.description, scopeKind: g.scopeKind,
      includeChildSites: g.includeChildSites, siteIds: [...g.siteIds],
      departmentIds: [...g.departmentIds], isSystem: g.isSystem,
    };
    this.isNew.set(false);
    this.error.set(null);
    this.editing.set(true);
  }

  close(): void { this.editing.set(false); }

  toggleSite(id: string): void {
    this.form.siteIds = this.form.siteIds.includes(id)
      ? this.form.siteIds.filter(s => s !== id)
      : [...this.form.siteIds, id];
  }

  save(): void {
    this.busy.set(true);
    this.error.set(null);
    const payload: SecurityGroupUpsert = {
      name: this.form.name, description: this.form.description, scopeKind: this.form.scopeKind,
      includeChildSites: this.form.includeChildSites,
      siteIds: this.form.siteIds, departmentIds: this.form.departmentIds,
    };
    const done = {
      next: () => { this.busy.set(false); this.editing.set(false); this.reload(); },
      error: (e: any) => { this.busy.set(false); this.error.set(e?.error?.detail ?? 'Save failed.'); },
    };
    if (this.isNew()) this.api.create(payload).subscribe(done);
    else if (this.editingId) this.api.update(this.editingId, payload).subscribe(done);
  }

  remove(g: SecurityGroup): void {
    if (!confirm(`Delete "${g.name}"? Members will lose the visibility it grants.`)) return;
    this.api.remove(g.id).subscribe({ next: () => this.reload() });
  }

  private reload(): void {
    this.api.list().subscribe(g => this.groups.set(g));
  }

  private blank() {
    return {
      name: '', description: '', scopeKind: DataScopeKind.Sites,
      includeChildSites: true, siteIds: [] as string[], departmentIds: [] as string[], isSystem: false,
    };
  }
}
