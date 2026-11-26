import { Button } from '@shared/components/Button/Button';
import { Spinner } from '@shared/components/Spinner/Spinner';
import { formatMoney } from '@shared/utils/money';
import type { BoatImageInfo, BoatRate, PublicSeason } from '@/lib/types';
import { PhotoReel } from './PhotoReel';
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

export interface BoatDetailsProps {
  id: string;
  boatName: string;
  state: MoreDetailsState;
  /** Where the photo reel (or the spinner or error in its place) goes: the card's image slot. */
  mediaClassName: string;
  /** Where the bedding and prices go: under the reel and tags. */
  bodyClassName: string;
}

/**
 * A boat card's More details (§11.2): the photo reel in place of the thumbnail, then the bedding description and the prices
 * table under the reel and tags. While `GET /api/boat-details` hasn't arrived a small spinner shows in their place; if it failed,
 * an error with Retry.
 */
export function BoatDetails({ id, boatName, state, mediaClassName, bodyClassName }: BoatDetailsProps) {
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
  return (
    <>
      <div id={`${id}-media`} className={mediaClassName}>
        <PhotoReel boatName={boatName} images={details.images} />
      </div>
      <div id={`${id}-body`} className={`${bodyClassName} ${styles.body}`} data-testid="boat-details">
        {details.beddingDescription && <p className={styles.bedding}>{details.beddingDescription}</p>}
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
      </div>
    </>
  );
}
