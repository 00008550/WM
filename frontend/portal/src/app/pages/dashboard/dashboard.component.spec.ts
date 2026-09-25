import { TestBed } from '@angular/core/testing';
import { EMPTY, Subject, of } from 'rxjs';
import { LivePresence, PunchRow, WorkforceApi } from '../../core/api/workforce.api';
import { PunchEvent, RealtimeService } from '../../core/realtime/realtime.service';
import { VIEWER_TIME_ZONE } from '../../shared/punch-time.pipe';
import { DashboardComponent } from './dashboard.component';

/**
 * 022 P2 — the dashboard shows a Ljubljana punch on the Ljubljana clock to a viewer in Tashkent.
 */
describe('DashboardComponent — punch times on the site clock', () => {
  const OBSERVED = '2026-09-17T19:44:00Z';
  const punches$ = new Subject<PunchEvent>();

  const row: PunchRow = {
    id: 'p1', employeeId: 'e1', employeeCode: 'E1001', employeeName: 'Ada Lovelace',
    timestamp: OBSERVED, direction: 0, source: 0, deviceId: null,
    localDate: '2026-09-17', localZone: 'Europe/Ljubljana',
  };

  const presence = (zones: string[]): LivePresence => ({
    presentCount: zones.length, activeEmployees: 10,
    present: zones.map((zone, i) => ({
      employeeId: `e${i}`, employeeCode: `E${i}`, employeeName: `Person ${i}`, jobTitle: null,
      siteId: 's1', since: OBSERVED, sinceLocalZone: zone, sinceLocalDate: '2026-09-17',
    })),
  });

  function render(viewerZone: string, present: LivePresence) {
    TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [
        { provide: VIEWER_TIME_ZONE, useValue: viewerZone },
        { provide: WorkforceApi, useValue: { livePresence: () => of(present), recentPunches: () => of([row]) } },
        { provide: RealtimeService, useValue: { punches$: punches$.asObservable(), scopeChanged$: EMPTY } },
      ],
    });
    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.detectChanges();
    return fixture;
  }

  const text = (el: Element) => el.textContent!.replace(/\s+/g, ' ').trim();
  const sections = (el: HTMLElement) => el.querySelectorAll('section');

  it('shows "Currently clocked in" as in 21:44 Ljubljana for a Tashkent viewer', () => {
    const fixture = render('Asia/Tashkent', presence(['Europe/Ljubljana']));
    const who = sections(fixture.nativeElement)[0];
    expect(text(who)).toContain('in 21:44 Ljubljana');
    expect(text(who)).not.toContain('00:44');
    fixture.destroy();
  });

  it('shows no label in "Currently clocked in" when every row is in the viewer zone', () => {
    const fixture = render('Europe/Ljubljana', presence(['Europe/Ljubljana']));
    const who = sections(fixture.nativeElement)[0];
    expect(text(who)).toContain('in 21:44');
    expect(text(who)).not.toContain('Ljubljana');
    fixture.destroy();
  });

  it('labels every row when the list mixes zones, including the one in the viewer zone', () => {
    const fixture = render('Europe/Ljubljana', presence(['Europe/Ljubljana', 'Asia/Tashkent']));
    const who = text(sections(fixture.nativeElement)[0]);
    expect(who).toContain('in 21:44 Ljubljana');
    expect(who).toContain('in 00:44 Tashkent');
    fixture.destroy();
  });

  it('shows the live feed on the site clock, initial load and realtime alike', () => {
    const fixture = render('Asia/Tashkent', presence([]));
    const feed = () => sections(fixture.nativeElement)[2];
    expect(text(feed())).toContain('21:44:00 Ljubljana');

    punches$.next({
      punchId: 'p2', employeeId: 'e2', employeeCode: 'E1002', employeeName: 'Grace Hopper',
      siteId: 's1', timestamp: '2026-09-17T19:45:30Z', direction: 'Out', source: 'Web',
      localDate: '2026-09-17', localZone: 'Europe/Ljubljana',
    });
    fixture.detectChanges();
    expect(text(feed())).toContain('21:45:30 Ljubljana');
    expect(text(feed())).not.toContain('00:4');
    fixture.destroy();
  });
});
