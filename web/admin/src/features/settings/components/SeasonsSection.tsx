import { useId, useState } from 'react';
import { useFieldArray } from 'react-hook-form';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Button } from '@shared/components/Button/Button';
import { Dialog } from '@shared/components/Dialog/Dialog';
import { Field, inputClass } from '@shared/components/Field/Field';
import { useToast } from '@shared/components/Toast/Toast';
import { rules, useAppForm } from '@shared/lib/forms';
import { ApiError, errorMessage } from '@shared/lib/http';
import { Section } from '@/components/Section';
import { table } from '@/components/Table/Table';
import { useSeasons } from '@/hooks/useReferenceData';
import { api, keys } from '@/lib/api';
import type { Season } from '@/lib/types';
import styles from './Settings.module.css';

export function SeasonsSection() {
  const seasons = useSeasons();
  const [editing, setEditing] = useState<Season | 'new' | null>(null);
  const queryClient = useQueryClient();
  const toast = useToast();
  const remove = useMutation({
    mutationFn: (s: Season) => api.deleteSeason(s.id),
    onSuccess: () => { void queryClient.invalidateQueries({ queryKey: keys.seasons }); toast.show('Season deleted.'); },
    onError: (e) => toast.show(errorMessage(e), 'error'),
  });
  return (
    <Section id="seasons" title="Seasons"
      help="Recurring day-month ranges (dd-MM) used for pricing. Ranges may wrap the year end (e.g. 01-12 → 31-01) and must not overlap. Dates not covered fall in Normal.">
      <table className={table.table}>
        <thead><tr><th>Season</th><th>Ranges</th><th /></tr></thead>
        <tbody>
          {seasons.data?.map((s) => (
            <tr key={s.id}>
              <td>{s.name}{s.isDefault && <span className={table.muted}> (default)</span>}</td>
              <td>{s.isDefault ? 'Everything else' : s.ranges.map((r) => `${r.start} → ${r.end}`).join(', ')}</td>
              <td>
                {!s.isDefault && (
                  <div className={table.actions}>
                    <Button size="sm" onClick={() => setEditing(s)}>Edit</Button>
                    <Button size="sm" variant="danger" onClick={() => remove.mutate(s)} busy={remove.isPending && remove.variables?.id === s.id} busyLabel="Deleting…">Delete</Button>
                  </div>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <div><Button onClick={() => setEditing('new')}>+ New season</Button></div>
      {editing && <SeasonDialog season={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </Section>
  );
}

interface Form {
  name: string;
  ranges: { start: string; end: string }[];
}

/** The server's dd-MM rule. Ranges may run over the new year (e.g. 01-12 → 31-01), so start and end are checked separately. */
function dayMonthError(text: string): string | true {
  const m = /^(\d{2})-(\d{2})$/.exec(text.trim());
  if (!m) return 'Use the format dd-MM, e.g. 01-12.';
  const day = Number(m[1]);
  const month = Number(m[2]);
  const daysInMonth = new Date(Date.UTC(2024, month, 0)).getUTCDate(); // a leap year allows 29-02
  return month >= 1 && month <= 12 && day >= 1 && day <= daysInMonth ? true : `${text} is not a valid day and month.`;
}

function SeasonDialog({ season, onClose }: { season: Season | null; onClose: () => void }) {
  const base = useId();
  const queryClient = useQueryClient();
  const toast = useToast();
  // Each field is validated when it loses focus (§6.1).
  const { register, control, handleSubmit, setError, formState: { errors } } = useAppForm<Form>({
    defaultValues: { name: season?.name ?? '', ranges: season?.ranges.length ? season.ranges : [{ start: '', end: '' }] },
  });
  const { fields, append, remove } = useFieldArray({ control, name: 'ranges' });
  const save = useMutation({
    mutationFn: (v: Form) => {
      const body = { name: v.name, ranges: v.ranges, version: season?.version ?? 0 };
      return season ? api.updateSeason(season.id, body) : api.createSeason(body);
    },
    onSuccess: () => { void queryClient.invalidateQueries({ queryKey: keys.seasons }); toast.show('Season saved.'); onClose(); },
    onError: (e) => {
      if (e instanceof ApiError && e.errors.name) setError('name', { message: e.errors.name[0] });
    },
  });
  const rangeError = save.error instanceof ApiError ? save.error.fieldError('ranges') : undefined;
  const submit = handleSubmit((v) => save.mutate(v));
  return (
    <Dialog open title={season ? `Edit ${season.name}` : 'New season'} onClose={onClose} locked={save.isPending} size="sm"
      footer={<>
        {save.isError && !rangeError && !errors.name && <span role="alert" className={styles.error}>{errorMessage(save.error)}</span>}
        <Button onClick={onClose} disabled={save.isPending}>Cancel</Button>
        <Button variant="primary" onClick={submit} busy={save.isPending} busyLabel="Saving…">Save</Button>
      </>}>
      <form onSubmit={submit} noValidate>
        <fieldset className={styles.fieldset} disabled={save.isPending}>
          <Field label="Name" htmlFor={`${base}-name`} error={errors.name?.message}>
            <input id={`${base}-name`} className={inputClass} {...register('name', { validate: { required: rules.required('Name is required.'), max: rules.maxLength(60, 'Name') } })} />
          </Field>
          <div className={styles.label}>Ranges (dd-MM)</div>
          {fields.map((f, i) => {
            const rowError = errors.ranges?.[i]?.start?.message ?? errors.ranges?.[i]?.end?.message;
            return (
              <div key={f.id}>
                <div className={styles.rangeRow}>
                  <input aria-label={`Range ${i + 1} start`} placeholder="01-12" className={inputClass} aria-invalid={!!errors.ranges?.[i]?.start || undefined}
                    {...register(`ranges.${i}.start`, { validate: dayMonthError })} />
                  <span>→</span>
                  <input aria-label={`Range ${i + 1} end`} placeholder="31-01" className={inputClass} aria-invalid={!!errors.ranges?.[i]?.end || undefined}
                    {...register(`ranges.${i}.end`, { validate: dayMonthError })} />
                  <Button size="sm" variant="danger" onClick={() => remove(i)} disabled={fields.length === 1}>Remove</Button>
                </div>
                {rowError && <div role="alert" className={styles.error}>{rowError}</div>}
              </div>
            );
          })}
          {rangeError && <div role="alert" className={styles.error}>{rangeError}</div>}
          <div><Button size="sm" onClick={() => append({ start: '', end: '' })}>+ Add range</Button></div>
        </fieldset>
      </form>
    </Dialog>
  );
}
