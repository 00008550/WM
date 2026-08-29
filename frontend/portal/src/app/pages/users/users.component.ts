import { Component, OnInit, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import {
  UsersApi, UserListItem, RoleListItem, CreateUserRequest, UpdateUserRequest,
} from '../../core/api/users.api';
import { WorkforceApi, EmployeeRow } from '../../core/api/workforce.api';
import {
  SecurityGroupsApi, SecurityGroup, AccessDiagnostics, DataScopeKind, SCOPE_LABELS,
} from '../../core/api/security-groups.api';
import { IconComponent } from '../../core/ui/icon.component';

@Component({
  selector: 'wm-users',
  imports: [FormsModule, IconComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="p-8 max-w-5xl">
      <header class="flex items-end justify-between gap-4 flex-wrap rise" style="--i: 0">
        <div>
          <h1 class="font-display text-2xl font-bold tracking-tight">Users</h1>
          <p class="text-muted text-sm mt-0.5"><span class="num">{{ total() }}</span> accounts · link a user to an employee for self-service</p>
        </div>
        <button (click)="openCreate()"
                class="flex items-center gap-2 px-3.5 py-2 rounded-md bg-pulse text-ink font-display font-bold text-sm
                       transition-transform duration-100 active:scale-[0.97]">
          <wm-icon name="plus" [size]="15" /> New user
        </button>
      </header>

      <div class="mt-6 rise" style="--i: 1">
        <table class="w-full text-sm">
          <thead>
            <tr class="text-left text-[11px] uppercase tracking-[0.18em] text-muted border-b border-line">
              <th class="py-3 pr-4 font-medium">User</th>
              <th class="py-3 pr-4 font-medium">Roles</th>
              <th class="py-3 pr-4 font-medium">Employee link</th>
              <th class="py-3 pr-4 font-medium">Status</th>
              <th class="py-3 font-medium"></th>
            </tr>
          </thead>
          <tbody class="divide-y divide-line/50">
            @if (loading()) {
              @for (i of [0,1,2,3]; track i) {
                <tr><td class="py-3.5 pr-4" colspan="5"><div class="skeleton h-4 w-full"></div></td></tr>
              }
            } @else {
              @for (u of users(); track u.id) {
                <tr class="hover:bg-raised/40 transition-colors duration-100">
                  <td class="py-3 pr-4">
                    <div>{{ u.displayName }}</div>
                    <div class="text-xs text-muted num">{{ u.userName }} · {{ u.email }}</div>
                  </td>
                  <td class="py-3 pr-4">
                    <div class="flex flex-wrap gap-1">
                      @for (r of u.roles; track r) {
                        <span class="font-mono text-[9px] px-1.5 py-0.5 rounded-sm border border-line bg-raised text-muted">{{ r }}</span>
                      }
                    </div>
                  </td>
                  <td class="py-3 pr-4">
                    @if (u.employeeId) {
                      <span class="inline-flex items-center gap-1 text-pulse text-xs"><wm-icon name="link" [size]="13" /> linked</span>
                    } @else {
                      <span class="text-muted text-xs">—</span>
                    }
                  </td>
                  <td class="py-3 pr-4">
                    <span class="font-mono text-[9px] px-1.5 py-0.5 rounded-sm border"
                          [class]="u.isLockedOut ? 'text-amber border-amber/40 bg-amber/10'
                                 : u.isActive ? 'text-pulse border-pulse/40 bg-pulse/10'
                                 : 'text-muted border-line bg-raised'">
                      {{ u.isLockedOut ? 'LOCKED' : u.isActive ? 'ACTIVE' : 'DISABLED' }}
                    </span>
                  </td>
                  <td class="py-3 text-right">
                    <button (click)="openEdit(u)" class="text-xs text-muted hover:text-text transition-colors">Edit</button>
                  </td>
                </tr>
              } @empty {
                <tr><td colspan="5" class="py-14 text-center text-muted">No users found.</td></tr>
              }
            }
          </tbody>
        </table>
      </div>
    </div>

    <!-- editor drawer -->
    @if (editing()) {
      <div class="fixed inset-0 z-40 bg-ink/70 backdrop-blur-sm" (click)="close()"></div>
      <aside class="fixed inset-y-0 right-0 z-50 w-full max-w-md bg-surface border-l border-line
                    overflow-y-auto p-6 flex flex-col gap-4" role="dialog" aria-modal="true">
        <div class="flex items-center justify-between">
          <h2 class="font-display text-lg font-bold">{{ isNew() ? 'New user' : 'Edit user' }}</h2>
          <button (click)="close()" aria-label="Close" class="text-muted hover:text-text text-lg">✕</button>
        </div>

        <label class="block">
          <span class="text-xs uppercase tracking-widest text-muted">Display name</span>
          <input [(ngModel)]="form.displayName" class="mt-1.5 w-full bg-raised border border-line rounded-md px-3 py-2 text-sm focus:border-pulse/60" />
        </label>

        @if (isNew()) {
          <label class="block">
            <span class="text-xs uppercase tracking-widest text-muted">Username</span>
            <input [(ngModel)]="form.userName" autocomplete="off" spellcheck="false"
                   class="mt-1.5 w-full bg-raised border border-line rounded-md px-3 py-2 text-sm num focus:border-pulse/60" />
          </label>
        }

        <label class="block">
          <span class="text-xs uppercase tracking-widest text-muted">Email</span>
          <input [(ngModel)]="form.email" type="email" autocomplete="off"
                 class="mt-1.5 w-full bg-raised border border-line rounded-md px-3 py-2 text-sm focus:border-pulse/60" />
        </label>

        @if (isNew()) {
          <label class="block">
            <span class="text-xs uppercase tracking-widest text-muted">Password</span>
            <input [(ngModel)]="form.password" type="text" autocomplete="off"
                   class="mt-1.5 w-full bg-raised border border-line rounded-md px-3 py-2 text-sm num focus:border-pulse/60" />
          </label>
        }

        <label class="block">
          <span class="text-xs uppercase tracking-widest text-muted">Linked employee (self-service)</span>
          <select [(ngModel)]="form.employeeId"
                  class="mt-1.5 w-full bg-raised border border-line rounded-md px-3 py-2 text-sm focus:border-pulse/60">
            <option [ngValue]="null">— none —</option>
            @for (e of employees(); track e.id) {
              <option [ngValue]="e.id">{{ e.firstName }} {{ e.lastName }} ({{ e.code }})</option>
            }
          </select>
        </label>

        <div>
          <span class="text-xs uppercase tracking-widest text-muted">Roles</span>
          <div class="mt-2 space-y-1.5">
            @for (r of roles(); track r.id) {
              <label class="flex items-center gap-2.5 text-sm cursor-pointer">
                <input type="checkbox" [checked]="form.roleIds.includes(r.id)" (change)="toggleRole(r.id)"
                       class="accent-pulse w-4 h-4" />
                <span>{{ r.name }}</span>
                <span class="text-xs text-muted">{{ r.description }}</span>
              </label>
            }
          </div>
        </div>

        @if (!isNew()) {
          <div>
            <span class="text-xs uppercase tracking-widest text-muted">Security groups (what they can see)</span>
            <div class="mt-2 space-y-1.5">
              @for (g of securityGroups(); track g.id) {
                <label class="flex items-center gap-2.5 text-sm cursor-pointer">
                  <input type="checkbox" [checked]="form.groupIds.includes(g.id)" (change)="toggleGroup(g.id)"
                         class="accent-pulse w-4 h-4" />
                  <span>{{ g.name }}</span>
                  <span class="text-xs text-muted">{{ scopeLabel(g.scopeKind) }}</span>
                </label>
              } @empty {
                <p class="text-xs text-muted">No groups defined yet.</p>
              }
            </div>
          </div>

          @if (diagnostics(); as d) {
            <div class="border border-line rounded-md bg-raised/50 px-3 py-2.5">
              <div class="text-[11px] uppercase tracking-widest text-muted">Effective visibility</div>
              <p class="text-xs mt-1">{{ d.explanation }}</p>
            </div>
          }

          <label class="flex items-center gap-2.5 text-sm cursor-pointer">
            <input type="checkbox" [(ngModel)]="form.isActive" class="accent-pulse w-4 h-4" /> Active
          </label>
        }

        @if (error()) { <p class="text-coral text-xs" role="alert">{{ error() }}</p> }

        <div class="mt-auto flex items-center gap-2 pt-4">
          <button (click)="save()" [disabled]="busy()"
                  class="flex-1 bg-pulse text-ink font-display font-bold rounded-md py-2.5 transition-transform active:scale-[0.99] disabled:opacity-40">
            {{ busy() ? 'Saving…' : 'Save' }}
          </button>
          @if (!isNew()) {
            <button (click)="resetPassword()" [disabled]="busy()"
                    class="flex items-center gap-1.5 px-3 py-2.5 rounded-md border border-line text-muted hover:text-text transition-colors text-sm">
              <wm-icon name="key" [size]="14" /> Reset password
            </button>
          }
        </div>
        @if (resetInfo()) { <p class="text-pulse text-xs">{{ resetInfo() }}</p> }
      </aside>
    }
  `,
})
export class UsersComponent implements OnInit {
  private readonly api = inject(UsersApi);
  private readonly workforce = inject(WorkforceApi);
  private readonly groupsApi = inject(SecurityGroupsApi);

  readonly loading = signal(true);
  readonly users = signal<UserListItem[]>([]);
  readonly total = signal(0);
  readonly roles = signal<RoleListItem[]>([]);
  readonly employees = signal<EmployeeRow[]>([]);
  readonly securityGroups = signal<SecurityGroup[]>([]);
  readonly diagnostics = signal<AccessDiagnostics | null>(null);

  readonly editing = signal(false);
  readonly isNew = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly resetInfo = signal<string | null>(null);
  private editingId: string | null = null;

  form = this.blankForm();

  ngOnInit(): void {
    forkJoin({
      roles: this.api.roles(),
      employees: this.workforce.employees('', 1, 200),
      groups: this.groupsApi.list(),
    }).subscribe(({ roles, employees, groups }) => {
      this.roles.set(roles);
      this.employees.set(employees.items);
      this.securityGroups.set(groups);
    });
    this.load();
  }

  scopeLabel(kind: DataScopeKind): string {
    return SCOPE_LABELS[kind] ?? '';
  }

  toggleGroup(id: string): void {
    this.form.groupIds = this.form.groupIds.includes(id)
      ? this.form.groupIds.filter(g => g !== id)
      : [...this.form.groupIds, id];
  }

  openCreate(): void {
    this.form = this.blankForm();
    this.isNew.set(true);
    this.error.set(null);
    this.resetInfo.set(null);
    this.editing.set(true);
  }

  openEdit(u: UserListItem): void {
    this.editingId = u.id;
    const roleIds = this.roles().filter(r => u.roles.includes(r.name)).map(r => r.id);
    this.form = {
      userName: u.userName, email: u.email, displayName: u.displayName, password: '',
      employeeId: u.employeeId, roleIds, isActive: u.isActive, groupIds: [],
      // Carried as read, never edited (011 P5): which version of this user was on screen.
      version: u.version,
    };
    this.isNew.set(false);
    this.error.set(null);
    this.resetInfo.set(null);
    this.diagnostics.set(null);
    this.editing.set(true);

    // Current membership and the resulting visibility, so an admin can see the
    // effect of a change without leaving the drawer.
    this.groupsApi.groupsForUser(u.id).subscribe(ids => (this.form.groupIds = ids));
    this.groupsApi.diagnostics(u.id).subscribe(d => this.diagnostics.set(d));
  }

  close(): void {
    this.editing.set(false);
  }

  toggleRole(id: string): void {
    this.form.roleIds = this.form.roleIds.includes(id)
      ? this.form.roleIds.filter(r => r !== id)
      : [...this.form.roleIds, id];
  }

  save(): void {
    this.busy.set(true);
    this.error.set(null);
    const done = { next: () => { this.busy.set(false); this.editing.set(false); this.load(); },
                   error: (e: any) => { this.busy.set(false); this.error.set(e?.error?.detail ?? 'Save failed.'); } };
    if (this.isNew()) {
      const req: CreateUserRequest = {
        userName: this.form.userName, email: this.form.email, displayName: this.form.displayName,
        password: this.form.password, employeeId: this.form.employeeId, roleIds: this.form.roleIds,
      };
      this.api.create(req).subscribe(done);
    } else if (this.editingId) {
      const id = this.editingId;
      const req: UpdateUserRequest = {
        displayName: this.form.displayName, email: this.form.email, isActive: this.form.isActive,
        employeeId: this.form.employeeId, roleIds: this.form.roleIds,
        version: this.form.version,
      };
      // Group membership is a separate endpoint; save it with the user so the
      // admin experiences one "Save" rather than two half-applied changes. A stale profile write is
      // refused before this runs, which is what keeps a conflict from half-applying: the membership
      // call is chained onto the profile call's success, so a 409 stops both.
      this.api.update(id, req).subscribe({
        next: () => this.groupsApi.setGroupsForUser(id, this.form.groupIds).subscribe(done),
        error: done.error,
      });
    }
  }

  resetPassword(): void {
    if (!this.editingId) return;
    const pwd = 'Wm' + Math.random().toString(36).slice(2, 8) + '!2';
    this.busy.set(true);
    this.api.resetPassword(this.editingId, pwd).subscribe({
      next: () => { this.busy.set(false); this.resetInfo.set(`New password: ${pwd} (copy it now — shown once)`); },
      error: e => { this.busy.set(false); this.error.set(e?.error?.detail ?? 'Reset failed.'); },
    });
  }

  private load(): void {
    this.api.list('', 1, 100).subscribe(r => {
      this.users.set(r.items);
      this.total.set(r.total);
      this.loading.set(false);
    });
  }

  private blankForm() {
    return {
      userName: '', email: '', displayName: '', password: '',
      employeeId: null as string | null, roleIds: [] as string[], isActive: true,
      groupIds: [] as string[], version: '',
    };
  }
}
