import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

/** Mirrors WM.SharedKernel.Security.DataScopeKind. */
export enum DataScopeKind {
  None = 0,
  Self = 1,
  Departments = 2,
  Sites = 3,
  All = 4,
}

export const SCOPE_LABELS: Record<DataScopeKind, string> = {
  [DataScopeKind.None]: 'No employee data',
  [DataScopeKind.Self]: 'Own record only',
  [DataScopeKind.Departments]: 'Chosen departments',
  [DataScopeKind.Sites]: 'Chosen sites',
  [DataScopeKind.All]: 'All employees',
};

export interface SecurityGroup {
  id: string;
  name: string;
  description: string;
  isSystem: boolean;
  scopeKind: DataScopeKind;
  includeChildSites: boolean;
  siteIds: string[];
  departmentIds: string[];
  memberCount: number;
}

export interface SecurityGroupUpsert {
  name: string;
  description: string;
  scopeKind: DataScopeKind;
  includeChildSites: boolean;
  siteIds: string[];
  departmentIds: string[];
}

export interface AccessDiagnostics {
  userId: string;
  scope: string;
  explanation: string;
  siteIds: string[];
  departmentIds: string[];
  selfEmployeeId: string | null;
}

@Injectable({ providedIn: 'root' })
export class SecurityGroupsApi {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiUrl;

  list(): Observable<SecurityGroup[]> {
    return this.http.get<SecurityGroup[]>(`${this.base}/api/security-groups`);
  }

  create(request: SecurityGroupUpsert): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(`${this.base}/api/security-groups`, request);
  }

  update(id: string, request: SecurityGroupUpsert): Observable<void> {
    return this.http.put<void>(`${this.base}/api/security-groups/${id}`, request);
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/api/security-groups/${id}`);
  }

  groupsForUser(userId: string): Observable<string[]> {
    return this.http.get<string[]>(`${this.base}/api/users/${userId}/security-groups`);
  }

  setGroupsForUser(userId: string, groupIds: string[]): Observable<void> {
    return this.http.put<void>(`${this.base}/api/users/${userId}/security-groups`, { groupIds });
  }

  diagnostics(userId: string): Observable<AccessDiagnostics> {
    return this.http.get<AccessDiagnostics>(`${this.base}/api/access-diagnostics/${userId}`);
  }
}
