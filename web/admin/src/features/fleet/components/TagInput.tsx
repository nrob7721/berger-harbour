import { useState, type KeyboardEvent } from 'react';
import { inputClass } from '@shared/components/Field/Field';
import styles from './TagInput.module.css';

/** The server's tag rules (§7.3). */
export const MAX_TAGS = 30;
export const MAX_TAG_LENGTH = 60;

/** Why `text` can't be added to `tags`, or null when it can. A blank entry is not an error: it adds nothing. */
export function tagProblem(text: string, tags: readonly string[]): string | null {
  const tag = text.trim();
  if (tag.length > MAX_TAG_LENGTH) return 'Keep each tag to 60 characters.';
  if (tags.some((t) => t.toLowerCase() === tag.toLowerCase())) return 'This tag is already on the boat.';
  if (tags.length >= MAX_TAGS) return 'A boat can have at most 30 tags.';
  return null;
}

export interface TagInputProps {
  id: string;
  tags: string[];
  onChange: (tags: string[]) => void;
  disabled?: boolean;
  /** A server error on the tags, shown until the input changes. */
  error?: string;
}

/**
 * The Fleet dialog's tag input (§12.2): the boat's tags as chips in their order, each with a × that removes it, followed by a
 * text cursor. Enter adds the typed text at the end and empties the input; a tag can't be edited or moved, only removed and typed
 * again. Enter never submits the dialog, and Backspace in the empty input removes nothing.
 */
export function TagInput({ id, tags, onChange, disabled, error }: TagInputProps) {
  const [text, setText] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const shown = problem ?? error;

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key !== 'Enter') return;
    e.preventDefault();
    const tag = text.trim();
    if (!tag) {
      setText('');
      return;
    }
    const why = tagProblem(tag, tags);
    if (why) {
      setProblem(why);
      return;
    }
    onChange([...tags, tag]);
    setText('');
    setProblem(null);
  };

  return (
    <div className={styles.tagInput}>
      <div className={`${inputClass} ${styles.box} ${shown ? styles.invalid : ''}`}>
        <ul className={styles.chips} aria-label="Current tags">
          {tags.map((t, i) => (
            <li key={t} className={styles.chip}>
              <span>{t}</span>
              <button type="button" className={styles.remove} aria-label={`Remove ${t}`} disabled={disabled}
                onClick={() => onChange(tags.filter((_, j) => j !== i))}>
                ×
              </button>
            </li>
          ))}
        </ul>
        <input
          id={id}
          className={styles.input}
          value={text}
          disabled={disabled}
          aria-invalid={shown ? true : undefined}
          aria-describedby={shown ? `${id}-error` : undefined}
          onChange={(e) => {
            setText(e.target.value);
            setProblem(null);
          }}
          onKeyDown={onKeyDown}
        />
      </div>
      {shown && <div className={styles.error} role="alert" id={`${id}-error`}>{shown}</div>}
    </div>
  );
}
