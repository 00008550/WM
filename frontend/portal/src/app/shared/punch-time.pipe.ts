import { ChangeDetectionStrategy, Component, InjectionToken, Pipe, PipeTransform, computed, inject, input } from '@angular/core';

/**
 * Punch times on the clock they were punched on (022 P2).
 *
 * A punch is shown in its own frozen zone (`localZone` / `sinceLocalZone` / `inZone` / `outZone`
 * from 022 P1), never the viewer's: a manager in Tashkent reading a Ljubljana site must see the
 * Ljubljana clock. When that zone differs from the viewer's — or a list mixes zones — the time
 * carries a city label, with the full IANA id as its tooltip. No abbreviations, no offsets.
 *
 * Days are never computed here from an instant in the browser's zone: they come from the API's
 * `localDate` as a string (see `punchDay`).
 */

/** The viewer's IANA zone. A token so specs pin it instead of inheriting the runner's. */
export const VIEWER_TIME_ZONE = new InjectionToken<string>('VIEWER_TIME_ZONE', {
  providedIn: 'root',
  factory: () => Intl.DateTimeFormat().resolvedOptions().timeZone,
});

/** Shown when a payload carries no zone. A defensive fallback — a current API never sends null. */
export const MISSING_ZONE_TITLE = 'UTC (zone missing)';

export type PunchTimeFormat = 'HH:mm' | 'HH:mm:ss';

/** `auto`: label only when the zone differs from the viewer's. `always`: mixed lists. */
export type ZoneLabelMode = 'auto' | 'always';

const UTC_ALIASES = new Set(['UTC', 'GMT', 'Etc/UTC', 'Etc/GMT', 'Etc/Universal', 'Etc/Zulu']);

/**
 * The city name for an IANA id: its last segment with `_` as spaces. UTC aliases read `UTC`;
 * any other cityless id (`Etc/GMT-5`, which means UTC+5) is shown unchanged rather than
 * inventing an offset. A null id reads `UTC`.
 */
export function zoneLabel(id: string | null | undefined): string {
  if (!id) return 'UTC';
  if (UTC_ALIASES.has(id)) return 'UTC';
  if (!id.includes('/') || id.startsWith('Etc/')) return id;
  return id.slice(id.lastIndexOf('/') + 1).replace(/_/g, ' ');
}

/** The tooltip for a zone: the full id, or the missing-zone note. */
export function zoneTitle(id: string | null | undefined): string {
  return id ? id : MISSING_ZONE_TITLE;
}

/** `HH:mm` / `HH:mm:ss` of an instant on the given zone's clock (UTC when the zone is missing). */
export function formatInZone(value: string | Date, zone: string | null | undefined, format: PunchTimeFormat = 'HH:mm'): string {
  const parts = new Intl.DateTimeFormat('en-GB', {
    timeZone: zone || 'UTC',
    hourCycle: 'h23',
    hour: '2-digit',
    minute: '2-digit',
    ...(format === 'HH:mm:ss' ? { second: '2-digit' as const } : {}),
  }).formatToParts(typeof value === 'string' ? new Date(value) : value);
  const part = (type: string) => parts.find(p => p.type === type)?.value ?? '00';
  return format === 'HH:mm:ss'
    ? `${part('hour')}:${part('minute')}:${part('second')}`
    : `${part('hour')}:${part('minute')}`;
}

/** Whether a time on `zone` needs its city label for this viewer. A missing zone always does. */
export function needsZoneLabel(zone: string | null | undefined, viewerZone: string, mode: ZoneLabelMode = 'auto'): boolean {
  return mode === 'always' || !zone || zone !== viewerZone;
}

/** True when the zones in a list are not all the same, so every row should carry its label. */
export function mixesZones(zones: readonly (string | null | undefined)[]): boolean {
  return new Set(zones.map(z => z ?? '')).size > 1;
}

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/**
 * The `d MMM` day of a punch. From `localDate` by plain string handling when the API sent it;
 * for a pre-008 row with no `localDate`, from the instant **in the punch's own zone** (the P1
 * review ruling) — never the browser's.
 */
export function punchDay(localDate: string | null | undefined, instant: string, zone: string | null | undefined): string {
  const iso = localDate ?? isoDateInZone(instant, zone);
  const [, month, day] = iso.split('-').map(Number);
  return `${day} ${MONTHS[month - 1]}`;
}

function isoDateInZone(instant: string, zone: string | null | undefined): string {
  // en-CA formats a date as yyyy-MM-dd.
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: zone || 'UTC', year: 'numeric', month: '2-digit', day: '2-digit',
  }).format(new Date(instant));
}

/**
 * `value | punchTime:zone:format:label` → `21:44` or `21:44 Ljubljana`. Text only; templates that
 * want the tooltip use `<wm-punch-time>`.
 */
@Pipe({ name: 'punchTime' })
export class PunchTimePipe implements PipeTransform {
  private readonly viewerZone = inject(VIEWER_TIME_ZONE);

  transform(value: string | null | undefined, zone: string | null | undefined,
            format: PunchTimeFormat = 'HH:mm', label: ZoneLabelMode = 'auto'): string {
    if (!value) return '';
    const time = formatInZone(value, zone, format);
    return needsZoneLabel(zone, this.viewerZone, label) ? `${time} ${zoneLabel(zone)}` : time;
  }
}

/** A punch time with its city label and the full IANA id as the tooltip. */
@Component({
  selector: 'wm-punch-time',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[attr.title]': 'title()' },
  template: `{{ time() }}@if (labelled()) {<span class="text-muted/70 font-sans"> {{ city() }}</span>}`,
})
export class PunchTimeComponent {
  private readonly viewerZone = inject(VIEWER_TIME_ZONE);

  readonly value = input.required<string>();
  readonly zone = input<string | null | undefined>(null);
  readonly format = input<PunchTimeFormat>('HH:mm');
  readonly label = input<ZoneLabelMode>('auto');

  readonly time = computed(() => formatInZone(this.value(), this.zone(), this.format()));
  readonly labelled = computed(() => needsZoneLabel(this.zone(), this.viewerZone, this.label()));
  readonly city = computed(() => zoneLabel(this.zone()));
  readonly title = computed(() => zoneTitle(this.zone()));
}
