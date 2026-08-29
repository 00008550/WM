import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Paged } from './workforce.api';

export interface UserListItem {
  id: string;
  userName: string;
  email: string;
  displayName: string;
  isActive: boolean;
  isLockedOut: boolean;
  employeeId: string | null;
  roles: string[];
  /** Optimistic-concurrency token (011 P5). Read here, echoed on `UpdateUserRequest.version`. */
  version: string;
}

export interface RoleListItem {
  id: string;
  name: string;
  description: string;
  isSystem: boolean;
}

export interface CreateUserRequest {
  userName: string;
  email: string;
  displayName: string;
  password: string;
  employeeId: string | null;
  roleIds: string[];
}

export interface UpdateUserRequest {
  displayName: string;
  email: string;
  isActive: boolean;
  employeeId: string | null;
  roleIds: string[];
  /**
   * The `version` this user was read with, echoed unchanged. **Required** — the API refuses an edit
   * that carries none, rather than falling back to the silent last-write-wins this replaced. A stale
   * one is a 409 whose `detail` is the message to show.
   */
  version: string;
}

@Injectable({ providedIn: 'root' })
export class UsersApi {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiUrl;

  list(search: string, page: number, pageSize = 25): Observable<Paged<UserListItem>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (search) params = params.set('search', search);
    return this.http.get<Paged<UserListItem>>(`${this.base}/api/users`, { params });
  }

  roles(): Observable<RoleListItem[]> {
    return this.http.get<RoleListItem[]>(`${this.base}/api/users/roles`);
  }

  create(request: CreateUserRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(`${this.base}/api/users`, request);
  }

  update(id: string, request: UpdateUserRequest): Observable<void> {
    return this.http.put<void>(`${this.base}/api/users/${id}`, request);
  }

  resetPassword(id: string, newPassword: string): Observable<void> {
    return this.http.post<void>(`${this.base}/api/users/${id}/reset-password`, { newPassword });
  }
}
