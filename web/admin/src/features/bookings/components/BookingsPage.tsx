import { useEffect, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Button } from '@shared/components/Button/Button';
import { formatDate, todayInSydney, type IsoDate } from '@shared/utils/dates';
import { useBoats } from '@/hooks/useReferenceData';
import { api, keys } from '@/lib/api';
import { rangeFor, step, switchView, today, type TimelineView } from '../utils/timeline';
import { BookingDialog } from './BookingDialog';
import { Timeline, TimelineLegend } from './Timeline';
import styles from './BookingsPage.module.css';

const VIEW_KEY = 'bh-admin-timeline-view';

function readView(): TimelineView {
  try {
    return window.localStorage.getItem(VIEW_KEY) === 'week' ? 'week' : 'month';
  } catch {
    return 'month';
  }
}

function writeView(view: TimelineView) {
  try {
    window.localStorage.setItem(VIEW_KEY, view);
  } catch {
    /* per-browser convenience only */
  }
}

type DialogState = { bookingId: string | null; initial?: { boatId?: string; day?: IsoDate } } | null;

export function BookingsPage({ todayIso = todayInSydney() }: { todayIso?: IsoDate }) {
  const [range, setRange] = useState(() => rangeFor(readView(), todayIso));
  const [includeInactive, setIncludeInactive] = useState(false);
  const [dialog, setDialog] = useState<DialogState>(null);
  const [q, setQ] = useState('');
  const [submitted, setSubmitted] = useState('');
  const searchBox = useRef<HTMLDivElement>(null);

  const boats = useBoats();
  // No placeholder data: a range without data shows the loading state, never another range's bars. A range already loaded shows
  // from the cache straight away and refreshes in the background; each range has its own key, so only the latest is ever shown.
  const timeline = useQuery({
    queryKey: keys.timeline(range.from, range.to, includeInactive),
    queryFn: () => api.timeline(range.from, range.to, includeInactive),
    staleTime: 15_000,
  });
  const search = useQuery({ queryKey: keys.search(submitted), queryFn: () => api.search(submitted), enabled: submitted.length >= 2, staleTime: 0 });

  useEffect(() => {
    if (!submitted) return;
    const close = (e: MouseEvent) => {
      if (!searchBox.current?.contains(e.target as Node)) setSubmitted('');
    };
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [submitted]);

  const setView = (view: TimelineView) => {
    writeView(view);
    setRange((r) => switchView(r, view, todayIso));
  };

  return (
    <div className={styles.page}>
      <div className={styles.toolbar}>
        <div className={styles.group}>
          <Button onClick={() => setRange((r) => step(r, -1))}>‹ Prev</Button>
          <Button onClick={() => setRange(today(range.view, todayIso))}>Today</Button>
          <div className={styles.segmented} role="group" aria-label="View">
            {(['week', 'month'] as const).map((v) => (
              <button key={v} type="button" aria-pressed={range.view === v} className={range.view === v ? styles.on : ''} onClick={() => setView(v)}>
                {v === 'week' ? 'Week' : 'Month'}
              </button>
            ))}
          </div>
          <Button onClick={() => setRange((r) => step(r, 1))}>Next ›</Button>
          <h1 className={styles.title} aria-live="polite">{range.title}</h1>
        </div>
        <div className={styles.group}>
          <label className={styles.check}>
            <input type="checkbox" checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} /> Show inactive
          </label>
          <div className={styles.search} ref={searchBox}>
            <form className={styles.searchForm} role="search" onSubmit={(e) => { e.preventDefault(); setSubmitted(q.trim()); }}>
              <input aria-label="Search bookings" placeholder="Search name, email or reference" value={q} onChange={(e) => setQ(e.target.value)} />
              <Button type="submit" busy={search.isFetching} busyLabel="Searching…">Search</Button>
            </form>
            {submitted.length >= 2 && search.data && (
              <ul className={styles.results} aria-label="Search results">
                {search.data.length === 0 && <li className={styles.noResults}>No bookings found.</li>}
                {search.data.map((r) => (
                  <li key={r.id}>
                    <button type="button" onClick={() => { setDialog({ bookingId: r.id }); setSubmitted(''); }}>
                      <strong>{r.reference ?? 'Pending payment'}</strong> · {r.customerName}
                      <span>{r.boatName} · {formatDate(r.startDate)} → {formatDate(r.endDate)} · {r.status}{r.isStandby ? ' (stand-by)' : ''}</span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
          <Button variant="primary" onClick={() => setDialog({ bookingId: null, initial: { day: todayIso } })}>+ New</Button>
        </div>
      </div>
      <TimelineLegend />
      <Timeline
        range={range}
        boats={boats.data}
        data={timeline.data}
        error={timeline.data || timeline.isFetching ? null : timeline.error}
        onRetry={() => void timeline.refetch()}
        todayIso={todayIso}
        onOpenBooking={(id) => setDialog({ bookingId: id })}
        onNewBooking={(boatId, day) => setDialog({ bookingId: null, initial: { boatId, day } })}
      />
      {dialog && <BookingDialog bookingId={dialog.bookingId} initial={dialog.initial} onClose={() => setDialog(null)} />}
    </div>
  );
}
