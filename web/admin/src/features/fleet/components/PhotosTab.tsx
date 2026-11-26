import { useFieldArray, useWatch, type Control, type FieldErrors, type UseFormRegister } from 'react-hook-form';
import { Button } from '@shared/components/Button/Button';
import { Field, inputClass } from '@shared/components/Field/Field';
import type { BoatForm } from '../utils/boat-form';
import { BoatThumb } from './BoatThumb';
import styles from './PhotosTab.module.css';

export const MAX_PHOTOS = 30;
export const MAX_DESCRIPTION = 120;
const IMAGE_HOSTS = ['bergerhouseboats.com.au', 'www.bergerhouseboats.com.au'];
export const IMAGE_URL_ERROR = 'Use an https:// link to an image on bergerhouseboats.com.au.';

/** The server's image URL rule (§7.3): an absolute https:// URL on the WordPress site. */
export function isValidImageUrl(value: string): boolean {
  try {
    const url = new URL(value.trim());
    return url.protocol === 'https:' && IMAGE_HOSTS.includes(url.hostname);
  } catch {
    return false;
  }
}

export interface PhotosTabProps {
  control: Control<BoatForm>;
  register: UseFormRegister<BoatForm>;
  errors: FieldErrors<BoatForm>;
  id: (field: string) => string;
}

/**
 * The Fleet dialog's Photos tab (§12.2): one row per photo, in order, with a preview, the image URL, an optional description,
 * ▲/▼ to move the row and Remove. The first row is the thumbnail image. The order is saved only with the boat.
 */
export function PhotosTab({ control, register, errors, id }: PhotosTabProps) {
  const listError = errors.images?.root?.message;
  const { fields, append, remove, move } = useFieldArray({ control, name: 'images' });
  const urls = useWatch({ control, name: 'images' }) ?? [];

  return (
    <div className={styles.photos}>
      <p className={styles.muted}>The first photo is the thumbnail image, shown on the boat cards and in Fleet. Changes are saved with the boat.</p>
      {fields.length === 0 && <p className={styles.muted}>No photos yet.</p>}
      <ol className={styles.rows}>
        {fields.map((f, i) => (
          <li key={f.id} className={styles.row} data-testid="photo-row">
            <div className={styles.side}>
              {i === 0 && <span className={styles.thumbLabel}>Thumbnail image</span>}
              <BoatThumb src={urls[i]?.url?.trim() || null} alt={`Photo ${i + 1} preview`} className={styles.preview} />
            </div>
            <div className={styles.fields}>
              <Field label="Image URL" htmlFor={id(`images-${i}-url`)} error={errors.images?.[i]?.url?.message} hint="Link to an image on bergerhouseboats.com.au">
                <input id={id(`images-${i}-url`)} type="url" className={inputClass}
                  {...register(`images.${i}.url`, { validate: (v) => isValidImageUrl(v) || IMAGE_URL_ERROR })} />
              </Field>
              <Field label="Description" htmlFor={id(`images-${i}-description`)} error={errors.images?.[i]?.description?.message} hint="Shown under the photo">
                <input id={id(`images-${i}-description`)} className={inputClass} {...register(`images.${i}.description`, {
                  validate: (v) => v.trim().length <= MAX_DESCRIPTION || 'Keep the description to 120 characters.',
                })} />
              </Field>
            </div>
            <div className={styles.actions}>
              <Button size="sm" iconOnly aria-label={`Move photo ${i + 1} up`} disabled={i === 0} onClick={() => move(i, i - 1)}>▲</Button>
              <Button size="sm" iconOnly aria-label={`Move photo ${i + 1} down`} disabled={i === fields.length - 1} onClick={() => move(i, i + 1)}>▼</Button>
              <Button size="sm" onClick={() => remove(i)} aria-label={`Remove photo ${i + 1}`}>Remove</Button>
            </div>
          </li>
        ))}
      </ol>
      {listError && <div role="alert" className={styles.error}>{listError}</div>}
      <div>
        <Button onClick={() => append({ url: '', description: '' }, { shouldFocus: true })} disabled={fields.length >= MAX_PHOTOS}>+ Add photo</Button>
        {fields.length >= MAX_PHOTOS && <span className={styles.muted}> A boat can have at most 30 photos.</span>}
      </div>
    </div>
  );
}
