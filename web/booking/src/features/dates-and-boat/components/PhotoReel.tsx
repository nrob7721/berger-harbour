import { useState } from 'react';
import { BoatImage } from '@/components/BoatImage/BoatImage';
import { useSwipe } from '@/hooks/useSwipe';
import type { BoatImageInfo } from '@/lib/types';
import styles from './PhotoReel.module.css';

export interface PhotoReelProps {
  boatName: string;
  /** The boat's photos in order; the first is the thumbnail image. */
  images: BoatImageInfo[];
}

/**
 * The More details photo reel (§11.2): one photo at a time with Prev/Next, a "{i} / {n}" counter and the photo's description as
 * a caption. A touch swipe changes photo with the calendar's swipe rule. Only the current photo and the next one are loaded,
 * then one more ahead as the customer moves. A boat without photos shows the placeholder alone.
 */
export function PhotoReel({ boatName, images }: PhotoReelProps) {
  const [index, setIndex] = useState(0);
  const [loadedUpTo, setLoadedUpTo] = useState(Math.min(1, images.length - 1));
  const count = images.length;
  const go = (to: number) => {
    if (to < 0 || to >= count) return;
    setIndex(to);
    setLoadedUpTo((n) => Math.max(n, Math.min(to + 1, count - 1)));
  };
  const swipe = useSwipe((d) => go(d === 'next' ? index + 1 : index - 1), count > 1);

  if (count === 0) {
    return (
      <div className={styles.reel} data-testid="photo-reel">
        <BoatImage src={null} alt={`${boatName} photo`} className={styles.frame} />
      </div>
    );
  }

  const caption = images[index].description;
  return (
    // The reel's buttons and swipes are its own controls: they never select the boat (§11.2).
    <div className={styles.reel} data-testid="photo-reel" role="group" aria-roledescription="carousel" aria-label={`${boatName} photos`}>
      <div className={styles.viewport} {...swipe}>
        {images.slice(0, loadedUpTo + 1).map((image, i) => (
          <BoatImage key={i} src={image.url} alt={image.description ?? `${boatName} photo ${i + 1}`}
            className={`${styles.frame} ${i === index ? '' : styles.hidden}`} eager />
        ))}
        {count > 1 && (
          <>
            <button type="button" className={`${styles.nav} ${styles.prev}`} aria-label="Previous photo" disabled={index === 0}
              onClick={(e) => { e.stopPropagation(); go(index - 1); }}>‹</button>
            <button type="button" className={`${styles.nav} ${styles.next}`} aria-label="Next photo" disabled={index === count - 1}
              onClick={(e) => { e.stopPropagation(); go(index + 1); }}>›</button>
            <span className={styles.counter} data-testid="photo-counter">{index + 1} / {count}</span>
          </>
        )}
      </div>
      {caption && <p className={styles.caption} data-testid="photo-caption">{caption}</p>}
    </div>
  );
}
