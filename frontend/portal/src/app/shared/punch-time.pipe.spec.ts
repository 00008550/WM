import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  MISSING_ZONE_TITLE, PunchTimeComponent, PunchTimePipe, VIEWER_TIME_ZONE,
  formatInZone, mixesZones, punchDay, zoneLabel,
} from './punch-time.pipe';

/**
 * 022 P2. Every assertion passes an explicit zone, and the viewer's zone is injected — so the
 * result can never come from the runner's own zone. The observed case: a punch at
 * 2026-09-17T19:44:00Z on a Europe/Ljubljana site reads 21:44 on the 17th, in any browser.
 */
const OBSERVED = '2026-09-17T19:44:00Z';

function pipeFor(viewerZone: string): PunchTimePipe {
  TestBed.configureTestingModule({ providers: [{ provide: VIEWER_TIME_ZONE, useValue: viewerZone }] });
  return TestBed.runInInjectionContext(() => new PunchTimePipe());
}

describe('punchTime pipe', () => {
  it('reads the same instant on each zone\'s own clock — Ljubljana 21:44, Tashkent 00:44', () => {
    const pipe = pipeFor('Europe/Ljubljana');
    expect(pipe.transform(OBSERVED, 'Europe/Ljubljana')).toBe('21:44');
    expect(pipe.transform(OBSERVED, 'Asia/Tashkent')).toBe('00:44 Tashkent');
  });

  it('labels a Ljubljana punch with its city for a viewer in Tashkent, and never with an abbreviation or offset', () => {
    const out = pipeFor('Asia/Tashkent').transform(OBSERVED, 'Europe/Ljubljana');
    expect(out).toBe('21:44 Ljubljana');
    expect(out).not.toMatch(/CEST|CET|GMT|\+0?2/);
  });

  it('hides the label when the punch zone is the viewer zone', () => {
    expect(pipeFor('Europe/Ljubljana').transform(OBSERVED, 'Europe/Ljubljana')).toBe('21:44');
  });

  it('labels every row when asked to (a mixed list), even in the viewer zone', () => {
    expect(pipeFor('Europe/Ljubljana').transform(OBSERVED, 'Europe/Ljubljana', 'HH:mm', 'always')).toBe('21:44 Ljubljana');
  });

  it('shows a missing zone as UTC, labelled', () => {
    expect(pipeFor('UTC').transform(OBSERVED, null)).toBe('19:44 UTC');
  });

  it('formats seconds on the zone clock', () => {
    expect(formatInZone('2026-09-17T19:44:07Z', 'Europe/Ljubljana', 'HH:mm:ss')).toBe('21:44:07');
    expect(formatInZone('2026-09-17T19:44:07Z', 'Asia/Tashkent', 'HH:mm:ss')).toBe('00:44:07');
  });
});

describe('zoneLabel', () => {
  const cases: [string | null, string][] = [
    ['Europe/Ljubljana', 'Ljubljana'],
    ['Asia/Tashkent', 'Tashkent'],
    ['America/Los_Angeles', 'Los Angeles'],
    ['America/Argentina/Buenos_Aires', 'Buenos Aires'],
    ['Etc/UTC', 'UTC'],
    ['Etc/GMT', 'UTC'],
    ['Etc/Universal', 'UTC'],
    ['Etc/Zulu', 'UTC'],
    ['UTC', 'UTC'],
    ['GMT', 'UTC'],
    ['Etc/GMT-5', 'Etc/GMT-5'],
    [null, 'UTC'],
  ];
  for (const [id, label] of cases) {
    it(`${id} → ${label}`, () => expect(zoneLabel(id)).toBe(label));
  }
});

describe('punchDay', () => {
  it('takes the day from localDate, not from the instant', () => {
    // 2026-09-18T00:30Z is the 18th in UTC and later; the API filed it under the 17th.
    expect(punchDay('2026-09-17', '2026-09-18T00:30:00Z', 'Asia/Tashkent')).toBe('17 Sep');
  });

  it('with no localDate, derives the day in the punch zone — never the browser\'s', () => {
    expect(punchDay(null, OBSERVED, 'Europe/Ljubljana')).toBe('17 Sep');
    expect(punchDay(null, OBSERVED, 'Asia/Tashkent')).toBe('18 Sep');
  });
});

describe('mixesZones', () => {
  it('is false for one zone and true for two', () => {
    expect(mixesZones(['Europe/Ljubljana', 'Europe/Ljubljana'])).toBeFalse();
    expect(mixesZones(['Europe/Ljubljana', 'Asia/Tashkent'])).toBeTrue();
  });
});

@Component({
  imports: [PunchTimeComponent],
  template: `<wm-punch-time [value]="value" [zone]="zone" />`,
})
class HostComponent {
  value = OBSERVED;
  zone: string | null = 'Europe/Ljubljana';
}

describe('<wm-punch-time>', () => {
  function render(viewerZone: string, zone: string | null): HTMLElement {
    TestBed.configureTestingModule({ providers: [{ provide: VIEWER_TIME_ZONE, useValue: viewerZone }] });
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.zone = zone;
    fixture.detectChanges();
    return fixture.nativeElement.querySelector('wm-punch-time');
  }

  it('renders the city label with the full IANA id as its title', () => {
    const el = render('Asia/Tashkent', 'Europe/Ljubljana');
    expect(el.textContent!.trim()).toBe('21:44 Ljubljana');
    expect(el.getAttribute('title')).toBe('Europe/Ljubljana');
  });

  it('drops the label in the viewer zone, keeps the title', () => {
    const el = render('Europe/Ljubljana', 'Europe/Ljubljana');
    expect(el.textContent!.trim()).toBe('21:44');
    expect(el.getAttribute('title')).toBe('Europe/Ljubljana');
  });

  it('shows a missing zone as UTC with the missing-zone tooltip', () => {
    const el = render('Europe/Ljubljana', null);
    expect(el.textContent!.trim()).toBe('19:44 UTC');
    expect(el.getAttribute('title')).toBe(MISSING_ZONE_TITLE);
  });
});
