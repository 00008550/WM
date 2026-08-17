import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface EmployeeRow {
  id: string;
  code: string;
  firstName: string;
  lastName: string;
  email: string | null;
  jobTitle: string | null;
  siteId: string;
  departmentId: string | null;
  /** First day of employment, inclusive. */
  employedFrom: string;
  /** Last day of employment, **inclusive**; null means open-ended. */
  employedUntil: string | null;
  isSuspended: boolean;
  leavingReasonId: string | null;
  leaverComments: string | null;
  /** Derived by the API from the window above, as at `asAt`. Never stored — see 007 P1. */
  status: number;
  isEmployed: boolean;
  /** The date `status` and `isEmployed` were computed for (the `employedOn` query, default today). */
  asAt: string;
}

export interface Site {
  id: string;
  name: string;
  parentId: string | null;
  timeZone: string;
}

export interface EmployeeUpsert {
  code: string;
  firstName: string;
  lastName: string;
  email: string | null;
  phone: string | null;
  jobTitle: string | null;
  siteId: string;
  departmentId: string | null;
  employedFrom: string | null;
  /**
   * Last day of employment, inclusive; null ends the leaver record and **clears the reason and
   * comments with it**, which is how someone is re-hired.
   */
  employedUntil?: string | null;
  isSuspended?: boolean;
  /** From `GET /api/leaving-reasons`. Only accepted alongside an `employedUntil`. */
  leavingReasonId?: string | null;
  leaverComments?: string | null;
}

/** A maintained, per-customer vocabulary. Ships empty; retired entries stay readable. */
export interface LeavingReason {
  id: string;
  name: string;
  isActive: boolean;
}

export interface LivePresenceEntry {
  employeeId: string;
  employeeCode: string;
  employeeName: string;
  jobTitle: string | null;
  siteId: string;
  since: string;
}

export interface LivePresence {
  presentCount: number;
  activeEmployees: number;
  present: LivePresenceEntry[];
}

export interface PunchRow {
  id: string;
  employeeId: string;
  employeeCode: string;
  employeeName: string;
  timestamp: string;
  direction: number; // 0 In, 1 Out
  source: number;
  deviceId: string | null;
}

@Injectable({ providedIn: 'root' })
export class WorkforceApi {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiUrl;

  employees(search: string, page: number, pageSize = 25): Observable<Paged<EmployeeRow>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (search) params = params.set('search', search);
    return this.http.get<Paged<EmployeeRow>>(`${this.base}/api/employees`, { params });
  }

  sites(): Observable<Site[]> {
    return this.http.get<Site[]>(`${this.base}/api/sites`);
  }

  leavingReasons(): Observable<LeavingReason[]> {
    return this.http.get<LeavingReason[]>(`${this.base}/api/leaving-reasons`);
  }

  createEmployee(request: EmployeeUpsert): Observable<EmployeeRow> {
    return this.http.post<EmployeeRow>(`${this.base}/api/employees`, request);
  }

  updateEmployee(id: string, request: EmployeeUpsert): Observable<EmployeeRow> {
    return this.http.put<EmployeeRow>(`${this.base}/api/employees/${id}`, request);
  }

  livePresence(): Observable<LivePresence> {
    return this.http.get<LivePresence>(`${this.base}/api/attendance/live`);
  }

  recentPunches(take = 30): Observable<PunchRow[]> {
    return this.http.get<PunchRow[]>(`${this.base}/api/punches/recent`, {
      params: new HttpParams().set('take', take),
    });
  }

  recordPunch(employeeCode: string, direction: 'In' | 'Out'): Observable<unknown> {
    return this.http.post(`${this.base}/api/punches`, {
      employeeCode,
      direction: direction === 'In' ? 0 : 1,
      source: 1, // Web
    });
  }
}
