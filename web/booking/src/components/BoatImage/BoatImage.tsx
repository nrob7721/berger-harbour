import { useState } from 'react';
import styles from './BoatImage.module.css';

/** The boat's image from the WordPress site, or a neutral placeholder when it is empty or fails to load. */
export function BoatImage({ src, alt, className }: { src: string | null | undefined; alt: string; className?: string }) {
  const [failed, setFailed] = useState(false);
  if (!src || failed) {
    return (
      <div className={[styles.placeholder, className].filter(Boolean).join(' ')} role="img" aria-label={`${alt} (no photo)`}>
        <svg viewBox="0 0 48 32" aria-hidden="true" width="48" height="32">
          <path d="M4 22h40l-5 6H9z M12 22v-7h20v7 M16 15v-5h10v5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinejoin="round" />
        </svg>
      </div>
    );
  }
  return <img src={src} alt={alt} className={[styles.image, className].filter(Boolean).join(' ')} loading="lazy" onError={() => setFailed(true)} />;
}
