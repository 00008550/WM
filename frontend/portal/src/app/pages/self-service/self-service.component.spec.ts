import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { SelfServiceApi, TimesheetDay } from '../../core/api/self-service.api';
import { PunchRow } from '../../core/api/workforce.api';
import { AuthService } from '../../core/auth/auth.service';
import { VIEWER_TIME_ZONE } from '../../shared/punch-time.pipe';
import { SelfServiceComponent } from './self-service.component';

/**
 * 022 P2 — /me reads the clock and the day the API gives it. The viewer sits in Tashkent, where the
 * browser alone would put the observed Ljubljana punch at 00:44 on the 18th.
 */
describe('SelfServiceComponent — punch times on the site clock', () => {
  const punch = (over: Partial<PunchRow> = {}): PunchRow => ({
    id: 'p1', employeeId: 'e1', employeeCode: 'E1001', employeeName: 'Ada Lovelace',
    timestamp: '2026-09-17T19:44:00Z', direction: 0, source: 0, deviceId: null,
    localDate: '2026-09-17', localZone: 'Europe/Ljubljana', ...over,
  });

  const day: TimesheetDay = {
    date: '2026-09-17',
    totalHours: 0,
    intervals: [{ in: '2026-09-17T19:44:00Z', out: null, hours: null, inZone: 'Europe/Ljubljana', outZone: null }],
  };

  function render(punches: PunchRow[]): HTMLElement {
    TestBed.configureTestingModule({
      imports: [SelfServiceComponent],
      providers: [
        { provide: VIEWER_TIME_ZONE, useValue: 'Asia/Tashkent' },
        { provide: AuthService, useValue: { displayName: () => 'Ada' } },
        {
          provide: SelfServiceApi,
          useValue: {
            myEmployee: () => of(null),
            myPunches: () => of(punches),
            myTimesheet: () => of([day]),
            punchSelf: () => of(null),
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(SelfServiceComponent);
    fixture.detectChanges();
    return fixture.nativeElement;
  }

  const text = (el: Element) => el.textContent!.replace(/\s+/g, ' ').trim();

  it('shows the observed punch as 21:44 Ljubljana under the 17 Sep heading in the timesheet', () => {
    const el = render([punch()]);
    const week = el.querySelectorAll('section')[1];
    const row = [...week.querySelectorAll('.py-3')].find(r => text(r).includes('17 Sep'))!;
    expect(row).withContext('a 17 Sep day row').toBeTruthy();
    expect(text(row)).toContain('21:44 Ljubljana');
    expect(text(row)).not.toContain('00:44');
    expect(row.querySelector('wm-punch-time')!.getAttribute('title')).toBe('Europe/Ljubljana');
  });

  it('shows "since 21:44 Ljubljana" in the status', () => {
    expect(text(render([punch()]))).toContain('since 21:44 Ljubljana');
  });

  it('takes the recent list\'s day from localDate in both directions, whatever the runner\'s zone', () => {
    // Two rows whose localDate disagrees with the instant's date in opposite directions. Deriving
    // the day from the instant in any single zone (UTC-12 to UTC+14) gets at least one wrong.
    // Row 1: 00:30Z on the 18th, filed under the 17th (a night shift).
    // Row 2: 23:30Z on the 17th, filed under the 18th.
    const el = render([
      punch({ id: 'p1', timestamp: '2026-09-18T00:30:00Z', localDate: '2026-09-17' }),
      punch({ id: 'p2', timestamp: '2026-09-17T23:30:00Z', localDate: '2026-09-18' }),
    ]);
    const rows = [...el.querySelectorAll('section')[2].querySelectorAll('span.num')].map(text);
    expect(rows.length).toBe(2);
    expect(rows[0]).toContain('17 Sep 02:30 Ljubljana');
    expect(rows[1]).toContain('18 Sep 01:30 Ljubljana');
  });
});
