import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { EmployeeRow, PunchRow } from './workforce.api';

export interface TimesheetInterval {
  in: string;
  out: string | null;
  hours: number | null;
}

export interface TimesheetDay {
  date: string;
  intervals: TimesheetInterval[];
  totalHours: number;
}

@Injectable({ providedIn: 'root' })
export class SelfServiceApi {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiUrl;

  myEmployee(): Observable<EmployeeRow> {
    return this.http.get<EmployeeRow>(`${this.base}/api/me/employee`);
  }

  myPunches(take = 20): Observable<PunchRow[]> {
    return this.http.get<PunchRow[]>(`${this.base}/api/me/punches`, {
      params: new HttpParams().set('take', take),
    });
  }

  myTimesheet(): Observable<TimesheetDay[]> {
    return this.http.get<TimesheetDay[]>(`${this.base}/api/me/timesheet`);
  }

  punchSelf(direction: 'In' | 'Out'): Observable<unknown> {
    return this.http.post(`${this.base}/api/me/punch`, {
      direction: direction === 'In' ? 0 : 1,
    });
  }
}
