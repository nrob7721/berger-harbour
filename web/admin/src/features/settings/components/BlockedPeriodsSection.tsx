import { useId, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button } from '@shared/components/Button/Button';
import { Dialog } from '@shared/components/Dialog/Dialog';
import { Field, inputClass } from '@shared/components/Field/Field';
import { useToast } from '@shared/components/Toast/Toast';
import { applyServerErrors } from '@shared/lib/forms';
import { errorMessage } from '@shared/lib/http';
import { formatDate } from '@shared/utils/dates';
import { Section } from '@/components/Section';
import { table } from '@/components/Table/Table';
import { api, keys } from '@/lib/api';
import type { BlockedPeriod } from '@/lib/types';
import styles from './Settings.module.css';

export function BlockedPeriodsSection() {
  const periods = useQuery({ queryKey: keys.blockedPeriods, queryFn: api.blockedPeriods, staleTime: 60_000 });
  const [editing, setEditing] = useState<BlockedPeriod | 'new' | null>(null);
  const queryClient = useQueryClient();
  const toast = useToast();
  const remove = useMutation({
    mutationFn: (p: BlockedPeriod) => api.deleteBlockedPeriod(p.id),
    onSuccess: () => { void queryClient.invalidateQueries({ queryKey: keys.blockedPeriods }); toast.show('Blocked period deleted.'); },
    onError: (e) => toast.show(errorMessage(e), 'error'),
  });
  return (
    <Section id="blocked-periods" title="Blocked periods">
      <div className={styles.info}>Enter each year's Christmas/New Year, Easter and long weekends. Customers must phone to book these.</div>
      <table className={table.table}>
        <thead><tr><th>Name</th><th>First night</th><th>Last night</th><th>Extended schedule</th><th /></tr></thead>
        <tbody>
          {periods.data?.length === 0 && <tr><td colSpan={5} className={table.muted}>None yet.</td></tr>}
          {periods.data?.map((p) => (
            <tr key={p.id}>
              <td>{p.name}</td>
              <td>{formatDate(p.firstNight)}</td>
              <td>{formatDate(p.lastNight)}</td>
              <td>{p.usesExtendedPaymentSchedule ? 'Yes' : 'No'}</td>
              <td>
                <div className={table.actions}>
                  <Button size="sm" onClick={() => setEditing(p)}>Edit</Button>
                  <Button size="sm" variant="danger" onClick={() => remove.mutate(p)} busy={remove.isPending && remove.variables?.id === p.id} busyLabel="Deleting…">Delete</Button>
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <div><Button onClick={() => setEditing('new')}>+ New blocked period</Button></div>
      {editing && <BlockedPeriodDialog period={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </Section>
  );
}

interface Form {
  name: string;
  firstNight: string;
  lastNight: string;
  usesExtendedPaymentSchedule: boolean;
}

function BlockedPeriodDialog({ period, onClose }: { period: BlockedPeriod | null; onClose: () => void }) {
  const base = useId();
  const queryClient = useQueryClient();
  const toast = useToast();
  const { register, handleSubmit, setError, formState: { errors } } = useForm<Form>({
    defaultValues: {
      name: period?.name ?? '', firstNight: period?.firstNight ?? '', lastNight: period?.lastNight ?? '',
      usesExtendedPaymentSchedule: period?.usesExtendedPaymentSchedule ?? false,
    },
  });
  const save = useMutation({
    mutationFn: (v: Form) => {
      const body = { ...v, version: period?.version ?? 0 };
      return period ? api.updateBlockedPeriod(period.id, body) : api.createBlockedPeriod(body);
    },
    onSuccess: () => { void queryClient.invalidateQueries({ queryKey: keys.blockedPeriods }); toast.show('Blocked period saved.'); onClose(); },
    onError: (e) => applyServerErrors(e, setError, ['name', 'firstNight', 'lastNight']),
  });
  const submit = handleSubmit((v) => {
    if (!v.firstNight) return setError('firstNight', { message: 'Enter the first night.' });
    if (!v.lastNight) return setError('lastNight', { message: 'Enter the last night.' });
    save.mutate(v);
  });
  return (
    <Dialog open title={period ? `Edit ${period.name}` : 'New blocked period'} onClose={onClose} locked={save.isPending} size="sm"
      footer={<>
        {save.isError && Object.keys(errors).length === 0 && <span role="alert" className={styles.error}>{errorMessage(save.error)}</span>}
        <Button onClick={onClose} disabled={save.isPending}>Cancel</Button>
        <Button variant="primary" onClick={submit} busy={save.isPending} busyLabel="Saving…">Save</Button>
      </>}>
      <form onSubmit={submit} noValidate>
        <fieldset className={styles.fieldset} disabled={save.isPending}>
          <Field label="Name" htmlFor={`${base}-n`} error={errors.name?.message}>
            <input id={`${base}-n`} className={inputClass} placeholder="Easter 2027" {...register('name')} />
          </Field>
          <Field label="First night" htmlFor={`${base}-f`} error={errors.firstNight?.message}>
            <input id={`${base}-f`} type="date" className={inputClass} {...register('firstNight')} />
          </Field>
          <Field label="Last night" htmlFor={`${base}-l`} error={errors.lastNight?.message}>
            <input id={`${base}-l`} type="date" className={inputClass} {...register('lastNight')} />
          </Field>
          <label className={styles.checkLabel}>
            <input type="checkbox" {...register('usesExtendedPaymentSchedule')} /> Extended payment schedule (50% at 120 days, 100% at 90 days)
          </label>
        </fieldset>
      </form>
    </Dialog>
  );
}
