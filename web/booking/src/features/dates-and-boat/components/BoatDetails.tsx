import { useState, type KeyboardEvent } from 'react';
import { Button } from '@shared/components/Button/Button';
import { Spinner } from '@shared/components/Spinner/Spinner';
import { formatMoney } from '@shared/utils/money';
import type { BoatImageInfo, BoatRate, PublicSeason } from '@/lib/types';
import styles from './BoatDetails.module.css';

/** What a boat's More details show: from `GET /api/boat-details`, or `GET /api/boats/{slug}` for an inactive requested boat. */
export interface MoreDetails {
  images: BoatImageInfo[];
  beddingDescription?: string | null;
  securityBond: number;
  depositAmount: number;
  rates: BoatRate[];
}

export type MoreDetailsState =
  | { status: 'loading' }
  | { status: 'error'; retry: () => void }
  | { status: 'ready'; details: MoreDetails; seasons: PublicSeason[] };

const COLUMNS = [
  { type: 'Midweek', label: 'Midweek' },
  { type: 'Weekend', label: 'Weekend' },
  { type: 'Week', label: 'Week' },
  { type: 'LongWeekend', label: 'Long Weekend' },
] as const;

/** "dd-MM" → "dd/MM". */
const dayMonth = (v: string) => v.replace('-', '/');

/** A season's ranges as "dd/MM – dd/MM", or "All other dates" for Normal (the season without ranges). */
export function seasonDates(season: PublicSeason): string {
  return season.ranges.length === 0 ? 'All other dates' : season.ranges.map((r) => `${dayMonth(r.start)} – ${dayMonth(r.end)}`).join(', ');
}

/**
 * Experiment: the feature tags are hidden everywhere for now (the closed cards' tags and the Features tab). The tags are still
 * loaded, saved and edited in Fleet; set this to true to show them again.
 */
export const SHOW_FEATURE_TAGS = false;

type TabKey = 'features' | 'prices' | 'bedding';

export interface BoatDetailsProps {
  id: string;
  /** The boat's tags: shown in the Features tab once the details are ready. */
  tags: string[];
  boatName: string;
  state: MoreDetailsState;
  /** Where the spinner or error goes while the details aren't ready: the card's image slot. */
  mediaClassName: string;
  /** Where the bedding and prices go: under the reel and tags. */
  bodyClassName: string;
}

/**
 * A boat card's More details (§11.2): the Features, Prices and Bedding tabs under the reel and tags (the card itself shows the
 * reel in the image slot once the details have arrived). While `GET /api/boat-details` hasn't arrived a small spinner shows in
 * the image slot; if it failed, an error with Retry.
 */
export function BoatDetails({ id, tags, boatName, state, mediaClassName, bodyClassName }: BoatDetailsProps) {
  const [tab, setTab] = useState<TabKey>('features');
  if (state.status !== 'ready') {
    return (
      <div id={`${id}-media`} className={`${mediaClassName} ${styles.pending}`} data-testid="boat-details-pending">
        {state.status === 'loading' ? (
          <Spinner label="Loading boat details" />
        ) : (
          <div className={styles.failed} role="alert">
            <p>Boat details couldn't be loaded.</p>
            <Button size="sm" data-card-control onClick={(e) => { e.stopPropagation(); state.retry(); }}>Retry</Button>
          </div>
        )}
      </div>
    );
  }

  const { details, seasons } = state;
  const rate = (seasonId: string, type: string) => details.rates.find((r) => r.seasonId === seasonId && r.periodType === type)?.price;
  const tabs: { key: TabKey; label: string }[] = [
    ...(SHOW_FEATURE_TAGS && tags.length > 0 ? [{ key: 'features' as const, label: 'Features' }] : []),
    { key: 'prices', label: 'Prices' },
    ...(details.beddingDescription ? [{ key: 'bedding' as const, label: 'Bedding' }] : []),
  ];
  const active = tabs.some((t) => t.key === tab) ? tab : tabs[0].key;
  const onKeyDown = (e: KeyboardEvent<HTMLButtonElement>) => {
    const step = e.key === 'ArrowRight' ? 1 : e.key === 'ArrowLeft' ? -1 : 0;
    if (!step) return;
    e.preventDefault();
    const next = tabs[(tabs.findIndex((t) => t.key === active) + step + tabs.length) % tabs.length].key;
    setTab(next);
    document.getElementById(`${id}-tab-${next}`)?.focus();
  };
  return (
    <>
      <div id={`${id}-body`} className={`${bodyClassName} ${styles.body}`} data-testid="boat-details">
        <div className={styles.tabList} role="tablist" aria-label={`${boatName} details`}>
          {tabs.map((t) => (
            <button
              key={t.key}
              type="button"
              role="tab"
              id={`${id}-tab-${t.key}`}
              aria-selected={active === t.key}
              aria-controls={`${id}-panel`}
              tabIndex={active === t.key ? 0 : -1}
              className={`${styles.tabButton} ${active === t.key ? styles.tabActive : ''}`}
              data-card-control
              onClick={() => setTab(t.key)}
              onKeyDown={onKeyDown}
            >
              {t.label}
            </button>
          ))}
        </div>
        <div id={`${id}-panel`} role="tabpanel" aria-labelledby={`${id}-tab-${active}`} className={styles.panel}>
          {active === 'features' && (
            <ul className={styles.tags} aria-label={`${boatName} features`}>
              {tags.map((t) => <li key={t} className={styles.tag}>{t}</li>)}
            </ul>
          )}
          {active === 'bedding' && <p className={styles.bedding}>{details.beddingDescription}</p>}
          {active === 'prices' && (
            <>
              <div className={styles.tableWrap}>
                <table className={styles.prices} data-testid="prices-table">
                  <caption className="visually-hidden">{boatName} prices</caption>
                  <thead>
                    <tr>
                      <th scope="col">Season</th>
                      {COLUMNS.map((c) => <th key={c.type} scope="col">{c.label}</th>)}
                    </tr>
                  </thead>
                  <tbody>
                    {seasons.map((s) => (
                      <tr key={s.id}>
                        <th scope="row">
                          <span className={styles.season}>{s.name}</span>
                          <span className={styles.dates}>{seasonDates(s)}</span>
                        </th>
                        {COLUMNS.map((c) => {
                          const price = rate(s.id, c.type);
                          return <td key={c.type}>{price === undefined ? '—' : formatMoney(price)}</td>;
                        })}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <p className={styles.note}>Long weekends are booked by phone.</p>
              <p className={styles.note}>Deposit {formatMoney(details.depositAmount)} · Security bond {formatMoney(details.securityBond)}</p>
            </>
          )}
        </div>
      </div>
    </>
  );
}
