import { useState } from 'react';
import { Button } from '@shared/components/Button/Button';
import { Spinner } from '@shared/components/Spinner/Spinner';
import { errorMessage } from '@shared/lib/http';
import { Badge } from '@/components/Badge/Badge';
import { PageHeader } from '@/components/Layout/Layout';
import { useBoats } from '@/hooks/useReferenceData';
import type { Boat } from '@/lib/types';
import { BoatDialog } from './BoatDialog';
import { BoatThumb } from './BoatThumb';
import styles from './FleetPage.module.css';

export function FleetPage() {
  const boats = useBoats();
  const [open, setOpen] = useState<{ boat: Boat | null } | null>(null);
  return (
    <div>
      <PageHeader title="Fleet" actions={<Button variant="primary" onClick={() => setOpen({ boat: null })}>+ New boat</Button>} />
      {boats.isLoading && <p><Spinner /> Loading boats…</p>}
      {boats.isError && <p role="alert">{errorMessage(boats.error)}</p>}
      <ul className={styles.grid}>
        {boats.data?.map((b) => (
          <li key={b.id}>
            <button type="button" className={styles.card} onClick={() => setOpen({ boat: b })} aria-label={`Edit ${b.name}`}>
              <BoatThumb src={b.imageUrl} alt={b.name} className={styles.image} />
              <div className={styles.body}>
                <div className={styles.titleRow}>
                  <span className={styles.name}>{b.name}</span>
                  {b.isActive ? <Badge tone="success">Active</Badge> : <Badge tone="muted">Inactive</Badge>}
                </div>
                <div className={styles.meta}>
                  Max {b.berth} guests · {b.adultBeds ?? '?'} adult + {b.kidsBeds ?? '?'} kids beds
                </div>
                {(b.hasSpa || b.isPetFriendly) && (
                  <div className={styles.badges}>
                    {b.hasSpa && <Badge>Spa</Badge>}
                    {b.isPetFriendly && <Badge>Pet friendly</Badge>}
                  </div>
                )}
                <code className={styles.slug}>{b.slug}</code>
              </div>
            </button>
          </li>
        ))}
      </ul>
      {open && <BoatDialog boat={open.boat} onClose={() => setOpen(null)} />}
    </div>
  );
}
