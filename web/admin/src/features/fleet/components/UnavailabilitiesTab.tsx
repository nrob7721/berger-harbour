import { useId, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button } from '@shared/components/Button/Button';
import { Dialog } from '@shared/components/Dialog/Dialog';
import { Field, inputClass } from '@shared/components/Field/Field';
import { useToast } from '@shared/components/Toast/Toast';
import { applyServerErrors, rules, useAppForm } from '@shared/lib/forms';
import { errorMessage } from '@shared/lib/http';
import { formatDate } from '@shared/utils/dates';
import { table } from '@/components/Table/Table';
import { api, keys } from '@/lib/api';
import type { Boat, Unavailability } from '@/lib/types';
import styles from './BoatDialog.module.css';

/** The boat's unavailabilities: a scrollable table with edit and delete, and "+ New Unavailability". */
export function UnavailabilitiesTab({ boat }: { boat: Boat }) {
  const list = useQuery({ queryKey: keys.unavailabilities(boat.id), queryFn: () => api.unavailabilities(boat.id), staleTime: 0 });
  const [editing, setEditing] = useState<Unavailability | 'new' | null>(null);
  const queryClient = useQueryClient();
  const toast = useToast();
  const remove = useMutation({
    mutationFn: (u: Unavailability) => api.deleteUnavailability(boat.id, u.id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: keys.unavailabilities(boat.id) });
      void queryClient.invalidateQueries({ queryKey: keys.timelineRoot });
      toast.show('Unavailability deleted.');
    },
    onError: (e) => toast.show(errorMessage(e), 'error'),
  });
  return (
    <div className={styles.unavailabilities}>
      <div className={styles.unavailHeader}>
        <span className={styles.muted}>Nights when {boat.name} can't be hired (maintenance, etc.).</span>
        <Button variant="primary" onClick={() => setEditing('new')}>+ New Unavailability</Button>
      </div>
      <div className={styles.scrollTable}>
        <table className={table.table}>
          <thead><tr><th>First night</th><th>Last night</th><th>Comments</th><th /></tr></thead>
          <tbody>
            {list.data?.length === 0 && <tr><td colSpan={4} className={table.muted}>None.</td></tr>}
            {list.data?.map((u) => (
              <tr key={u.id}>
                <td>{formatDate(u.firstNight)}</td>
                <td>{formatDate(u.lastNight)}</td>
                <td>{u.comments}</td>
                <td>
                  <div className={table.actions}>
                    <Button size="sm" onClick={() => setEditing(u)}>Edit</Button>
                    <Button size="sm" variant="danger" onClick={() => remove.mutate(u)} busy={remove.isPending && remove.variables?.id === u.id} busyLabel="Deleting…">
                      Delete
                    </Button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {editing && <UnavailabilityDialog boat={boat} existing={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </div>
  );
}

interface UForm {
  firstNight: string;
  lastNight: string;
  comments: string;
}

/** First night ≤ last night; the error belongs to Last night. */
const lastNotBeforeFirst = (last: string, form: { firstNight: string }) =>
  !last || !form.firstNight || last >= form.firstNight || "The last night can't be before the first night.";

function UnavailabilityDialog({ boat, existing, onClose }: { boat: Boat; existing: Unavailability | null; onClose: () => void }) {
  const base = useId();
  const queryClient = useQueryClient();
  const toast = useToast();
  // Each field is validated when it loses focus (§6.1).
  const { register, handleSubmit, setError, formState: { errors } } = useAppForm<UForm>({
    defaultValues: { firstNight: existing?.firstNight ?? '', lastNight: existing?.lastNight ?? '', comments: existing?.comments ?? '' },
  });
  const save = useMutation({
    mutationFn: (v: UForm) => {
      const body = { firstNight: v.firstNight, lastNight: v.lastNight, comments: v.comments || null, version: existing?.version ?? 0 };
      return existing ? api.updateUnavailability(boat.id, existing.id, body) : api.createUnavailability(boat.id, body);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: keys.unavailabilities(boat.id) });
      void queryClient.invalidateQueries({ queryKey: keys.timelineRoot });
      toast.show('Unavailability saved.');
      onClose();
    },
    onError: (e) => applyServerErrors(e, setError, ['firstNight', 'lastNight', 'comments']),
  });
  const submit = handleSubmit((v) => save.mutate(v));
  return (
    <Dialog open title={existing ? 'Edit unavailability' : 'New unavailability'} onClose={onClose} locked={save.isPending} size="sm"
      footer={<>
        {save.isError && <span role="alert" className={styles.error}>{errorMessage(save.error)}</span>}
        <Button onClick={onClose} disabled={save.isPending}>Cancel</Button>
        <Button variant="primary" onClick={submit} busy={save.isPending} busyLabel="Saving…">Save</Button>
      </>}>
      <form onSubmit={submit} noValidate>
        <fieldset className={styles.fieldset} disabled={save.isPending}>
          <Field label="First night" htmlFor={`${base}-f`} error={errors.firstNight?.message}>
            <input id={`${base}-f`} type="date" className={inputClass} {...register('firstNight', { validate: rules.required('Enter the first night.'), deps: 'lastNight' })} />
          </Field>
          <Field label="Last night" htmlFor={`${base}-l`} error={errors.lastNight?.message}>
            <input id={`${base}-l`} type="date" className={inputClass}
              {...register('lastNight', { validate: { required: rules.required('Enter the last night.'), order: lastNotBeforeFirst } })} />
          </Field>
          <Field label="Comments" htmlFor={`${base}-c`} error={errors.comments?.message}>
            <input id={`${base}-c`} className={inputClass} {...register('comments', { validate: rules.maxLength(1000, 'Comments') })} />
          </Field>
        </fieldset>
      </form>
    </Dialog>
  );
}
