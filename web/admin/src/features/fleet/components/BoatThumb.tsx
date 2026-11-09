import { useEffect, useState } from 'react';
import styles from './BoatThumb.module.css';

/** The boat's WordPress image, or a neutral placeholder when it is empty or fails to load. */
export function BoatThumb({ src, alt, className }: { src: string | null | undefined; alt: string; className?: string }) {
  const [failed, setFailed] = useState(false);
  useEffect(() => setFailed(false), [src]);
  if (!src || failed) return <div className={`${styles.placeholder} ${className ?? ''}`} role="img" aria-label={`${alt} (no photo)`}>No photo</div>;
  return <img src={src} alt={alt} className={`${styles.image} ${className ?? ''}`} loading="lazy" onError={() => setFailed(true)} />;
}
