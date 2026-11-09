import type { CSSProperties } from 'react';
import { dayOfMonth, dayOfWeek, formatDate, weekdayShort, type IsoDate } from '@shared/utils/dates';
import type { Timeline as TimelineData, TimelineBooking } from '@/lib/types';
import { assignLanes, barPosition, dayBand, MIN_COLUMN_PX, unavailabilityPosition, type BarPosition, type TimelineRange } from '../utils/timeline';
import styles from './Timeline.module.css';

const LANE_PX = 28;
const STATUS_CLASS: Record<string, string> = { Outstanding: 'outstanding', DepositPaid: 'depositPaid', PartPaid: 'partPaid', FullyPaid: 'fullyPaid' };

export function barLabel(b: Pick<TimelineBooking, 'customerName' | 'reference'>): string {
  return `${b.customerName} · ${b.reference ?? 'Pending payment'}`;
}

const pos = (p: BarPosition): CSSProperties => ({ left: `${p.left}%`, width: `${p.width}%` });

export interface TimelineProps {
  range: TimelineRange;
  data: TimelineData | undefined;
  todayIso: IsoDate;
  onOpenBooking: (id: string) => void;
  onNewBooking: (boatId: string, date: IsoDate) => void;
}

/** One row per boat, one column per day. Bars are positioned by time of day and stacked into lanes. */
export function Timeline({ range, data, todayIso, onOpenBooking, onNewBooking }: TimelineProps) {
  const n = range.days.length;
  const columns = { gridTemplateColumns: `repeat(${n}, minmax(0, 1fr))` };
  const minWidth = range.view === 'month' ? `calc(170px + ${n * MIN_COLUMN_PX}px)` : undefined;
  const todayIndex = range.days.indexOf(todayIso);
  const todayLine = todayIndex >= 0 ? <div className={styles.today} style={{ left: `${(todayIndex / n) * 100}%` }} aria-hidden="true" /> : null;
  const weekend = (d: IsoDate) => dayOfWeek(d) === 0 || dayOfWeek(d) === 6;

  return (
    <div className={styles.scroller} data-testid="timeline-scroller">
      <div className={styles.grid} style={{ minWidth }} data-testid="timeline-grid" role="table" aria-label="Bookings timeline">
        <div className={`${styles.row} ${styles.headerRow}`} role="row">
          <div className={`${styles.name} ${styles.corner}`} role="columnheader">Boat</div>
          <div className={styles.days} style={columns}>
            {range.days.map((d) => (
              <div key={d} role="columnheader" className={`${styles.dayHead} ${weekend(d) ? styles.weekend : ''}`} aria-label={formatDate(d)}>
                <span>{weekdayShort(d).slice(0, 2)}</span>
                <strong>{dayOfMonth(d)}</strong>
              </div>
            ))}
          </div>
        </div>

        <div className={styles.row} role="row">
          <div className={`${styles.name} ${styles.muted}`} role="rowheader">Blocked periods</div>
          <div className={styles.track} style={{ height: LANE_PX + 8 }}>
            <div className={styles.cells} style={columns}>
              {range.days.map((d) => <div key={d} className={`${styles.cell} ${weekend(d) ? styles.weekend : ''}`} />)}
            </div>
            {data?.blockedPeriods.map((p) => {
              const band = dayBand(range, p.firstNight, p.lastNight);
              return band && <div key={p.id} className={styles.blocked} style={pos(band)} title={p.name}>{p.name}</div>;
            })}
            {todayLine}
          </div>
        </div>

        {data?.boats.map((boat) => {
          const bookings = data.bookings.filter((b) => b.boatId === boat.id);
          const { lanes, count } = assignLanes(bookings);
          return (
            <div key={boat.id} className={styles.row} role="row" data-testid={`row-${boat.slug}`}>
              <div className={`${styles.name} ${boat.isActive ? '' : styles.muted}`} role="rowheader">{boat.name}</div>
              <div className={styles.track} style={{ height: count * LANE_PX + 8 }}>
                <div className={styles.cells} style={columns}>
                  {range.days.map((d) => (
                    <button
                      key={d}
                      type="button"
                      className={`${styles.cell} ${weekend(d) ? styles.weekend : ''}`}
                      aria-label={`New booking for ${boat.name} on ${formatDate(d)}`}
                      onClick={() => onNewBooking(boat.id, d)}
                    />
                  ))}
                </div>
                {data.blockedPeriods.map((p) => {
                  const band = dayBand(range, p.firstNight, p.lastNight);
                  return band && <div key={p.id} className={styles.blockedShade} style={pos(band)} aria-hidden="true" />;
                })}
                {data.unavailabilities.filter((u) => u.boatId === boat.id).map((u) => {
                  const p = unavailabilityPosition(range, u.firstNight, u.lastNight);
                  return p && (
                    <div key={u.id} className={styles.unavailable} style={{ ...pos(p), top: 4, height: count * LANE_PX }}
                      title={`Unavailable ${formatDate(u.firstNight)} – ${formatDate(u.lastNight)}${u.comments ? `: ${u.comments}` : ''}`}>
                      {u.comments ?? 'Unavailable'}
                    </div>
                  );
                })}
                {bookings.map((b) => {
                  const p = barPosition(range, b.startDate, b.checkInTime, b.endDate, b.checkOutTime);
                  if (!p) return null;
                  const inactive = b.status === 'Cancelled' || b.status === 'Expired' || b.status === 'Superseded';
                  const cls = [
                    styles.bar,
                    styles[STATUS_CLASS[b.paymentStatus]],
                    b.status === 'PendingPayment' ? styles.pending : '',
                    b.isStandby ? styles.standby : '',
                    inactive ? styles.inactive : '',
                  ].join(' ');
                  return (
                    <button
                      key={b.id}
                      type="button"
                      className={cls}
                      style={{ ...pos(p), top: 4 + (lanes.get(b.id) ?? 0) * LANE_PX }}
                      onClick={() => onOpenBooking(b.id)}
                      title={`${barLabel(b)} — ${formatDate(b.startDate)} to ${formatDate(b.endDate)}${inactive ? ` (${b.status})` : ''}`}
                      data-testid={`bar-${b.id}`}
                      data-lane={lanes.get(b.id)}
                    >
                      {barLabel(b)}
                    </button>
                  );
                })}
                {todayLine}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

export function TimelineLegend() {
  return (
    <ul className={styles.legend} aria-label="Legend">
      <li><span className={`${styles.swatch} ${styles.outstanding}`} />Outstanding</li>
      <li><span className={`${styles.swatch} ${styles.depositPaid}`} />Deposit paid</li>
      <li><span className={`${styles.swatch} ${styles.partPaid}`} />Part paid</li>
      <li><span className={`${styles.swatch} ${styles.fullyPaid}`} />Fully paid</li>
      <li><span className={`${styles.swatch} ${styles.pendingSwatch}`} />Pending payment</li>
      <li><span className={`${styles.swatch} ${styles.standbySwatch}`} />Stand-by</li>
      <li><span className={`${styles.swatch} ${styles.unavailableSwatch}`} />Unavailable</li>
      <li><span className={`${styles.swatch} ${styles.blockedSwatch}`} />Blocked period</li>
    </ul>
  );
}
