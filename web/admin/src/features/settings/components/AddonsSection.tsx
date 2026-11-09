import { useEffect, useId, useState } from 'react';
import { useWatch } from 'react-hook-form';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Button } from '@shared/components/Button/Button';
import { Dialog } from '@shared/components/Dialog/Dialog';
import { Field, inputClass } from '@shared/components/Field/Field';
import { useToast } from '@shared/components/Toast/Toast';
import { applyServerErrors, rules, useAppForm } from '@shared/lib/forms';
import { errorMessage } from '@shared/lib/http';
import { formatMoney } from '@shared/utils/money';
import { Section } from '@/components/Section';
import { table } from '@/components/Table/Table';
import { useAddons } from '@/hooks/useReferenceData';
import { api, keys } from '@/lib/api';
import type { Addon } from '@/lib/types';
import styles from './Settings.module.css';

export function AddonsSection() {
  const addons = useAddons();
  const [editing, setEditing] = useState<Addon | 'new' | null>(null);
  const queryClient = useQueryClient();
  const toast = useToast();
  const list = addons.data ?? [];

  const reorder = useMutation({
    mutationFn: (ids: string[]) => api.reorderAddons(ids),
    onSuccess: (saved) => queryClient.setQueryData(keys.addons, saved),
    onError: (e) => toast.show(errorMessage(e), 'error'),
  });
  const move = (index: number, delta: -1 | 1) => {
    const ids = list.map((a) => a.id);
    [ids[index], ids[index + delta]] = [ids[index + delta], ids[index]];
    reorder.mutate(ids);
  };
  const toggle = useMutation({
    mutationFn: (a: Addon) => (a.isActive ? api.deactivateAddon(a.id) : api.updateAddon(a.id, { ...a, isActive: true })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: keys.addons }),
    onError: (e) => toast.show(errorMessage(e), 'error'),
  });

  return (
    <Section id="add-ons" title="Add-ons" help="This order is used in the booking app, the add-on picker in bookings and in emails.">
      <div className={table.scroll}>
        <table className={table.table}>
          <thead><tr><th>Order</th><th>Add-on</th><th>Mid-week / Weekend / Week</th><th>Quantity</th><th>Capacity</th><th>Status</th><th /></tr></thead>
          <tbody>
            {list.map((a, i) => (
              <tr key={a.id} className={a.isActive ? '' : table.faded}>
                <td className={styles.order}>
                  <button type="button" aria-label={`Move ${a.name} up`} disabled={i === 0 || reorder.isPending} onClick={() => move(i, -1)}>▲</button>
                  <button type="button" aria-label={`Move ${a.name} down`} disabled={i === list.length - 1 || reorder.isPending} onClick={() => move(i, 1)}>▼</button>
                </td>
                <td>{a.name}</td>
                <td>{a.priceOnRequest ? 'Price on request' : `${formatMoney(a.midweekPrice)} / ${formatMoney(a.weekendPrice)} / ${formatMoney(a.weekPrice)}`}</td>
                <td>{a.quantityApplies ? 'Yes' : 'No'}</td>
                <td>{a.capacity === null ? '—' : `${a.capacity} ${a.capacityUnitLabel ?? ''}`.trim()}</td>
                <td>{a.isActive ? 'Active' : 'Inactive'}</td>
                <td>
                  <div className={table.actions}>
                    <Button size="sm" onClick={() => setEditing(a)}>Edit</Button>
                    <Button size="sm" variant={a.isActive ? 'danger' : 'secondary'} onClick={() => toggle.mutate(a)}
                      busy={toggle.isPending && toggle.variables?.id === a.id} busyLabel={a.isActive ? 'Deactivating…' : 'Activating…'}>
                      {a.isActive ? 'Deactivate' : 'Activate'}
                    </Button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div><Button onClick={() => setEditing('new')}>+ New add-on</Button></div>
      {editing && <AddonDialog addon={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </Section>
  );
}

export interface AddonForm {
  name: string;
  description: string;
  priceOnRequest: boolean;
  midweekPrice: number | null;
  weekendPrice: number | null;
  weekPrice: number | null;
  quantityApplies: boolean;
  capacity: number | null;
  capacityUnitLabel: string;
}

const numberOrNull = (v: unknown) => (v === '' || v === null || v === undefined ? null : Number(v));
const FIELDS = ['name', 'description', 'midweekPrice', 'weekendPrice', 'weekPrice', 'capacity', 'capacityUnitLabel'] as const;

/** Ticking Price on request disables and clears the prices; an empty Capacity disables and clears the unit label. */
export function AddonDialog({ addon, onClose }: { addon: Addon | null; onClose: () => void }) {
  const base = useId();
  const id = (f: string) => `${base}-${f}`;
  const queryClient = useQueryClient();
  const toast = useToast();
  // Each field is validated when it loses focus (§6.1).
  const { register, handleSubmit, setError, setValue, control, formState: { errors } } = useAppForm<AddonForm>({
    defaultValues: {
      name: addon?.name ?? '', description: addon?.description ?? '', priceOnRequest: addon?.priceOnRequest ?? false,
      midweekPrice: addon?.midweekPrice ?? null, weekendPrice: addon?.weekendPrice ?? null, weekPrice: addon?.weekPrice ?? null,
      quantityApplies: addon?.quantityApplies ?? false, capacity: addon?.capacity ?? null, capacityUnitLabel: addon?.capacityUnitLabel ?? '',
    },
  });
  const [priceOnRequest, capacity] = useWatch({ control, name: ['priceOnRequest', 'capacity'] });
  const noCapacity = capacity === null || capacity === undefined || Number.isNaN(capacity);

  useEffect(() => {
    if (priceOnRequest) for (const f of ['midweekPrice', 'weekendPrice', 'weekPrice'] as const) setValue(f, null);
  }, [priceOnRequest, setValue]);
  useEffect(() => {
    if (noCapacity) setValue('capacityUnitLabel', '');
  }, [noCapacity, setValue]);

  const save = useMutation({
    mutationFn: (v: AddonForm) => {
      const body = {
        ...v, description: v.description || null, capacityUnitLabel: v.capacityUnitLabel || null,
        isActive: addon?.isActive ?? true, version: addon?.version ?? 0,
      };
      return addon ? api.updateAddon(addon.id, body) : api.createAddon(body);
    },
    onSuccess: () => { void queryClient.invalidateQueries({ queryKey: keys.addons }); toast.show('Add-on saved.'); onClose(); },
    onError: (e) => applyServerErrors(e, setError, FIELDS),
  });
  const submit = handleSubmit((v) => save.mutate(v));

  return (
    <Dialog open title={addon ? `Edit ${addon.name}` : 'New add-on'} onClose={onClose} locked={save.isPending} size="md"
      footer={<>
        {save.isError && Object.keys(errors).length === 0 && <span role="alert" className={styles.error}>{errorMessage(save.error)}</span>}
        <Button onClick={onClose} disabled={save.isPending}>Cancel</Button>
        <Button variant="primary" onClick={submit} busy={save.isPending} busyLabel="Saving…">Save</Button>
      </>}>
      <form onSubmit={submit} noValidate>
        <fieldset className={styles.fieldset} disabled={save.isPending}>
          <Field label="Name" htmlFor={id('name')} error={errors.name?.message}>
            <input id={id('name')} className={inputClass} {...register('name', { validate: { required: rules.required('Name is required.'), max: rules.maxLength(100, 'Name') } })} />
          </Field>
          <Field label="Description" htmlFor={id('desc')} error={errors.description?.message}>
            <textarea id={id('desc')} className={inputClass} rows={2} {...register('description', { validate: rules.maxLength(1000, 'Description') })} />
          </Field>
          <label className={styles.checkLabel}>
            <input type="checkbox" {...register('priceOnRequest', { deps: ['midweekPrice', 'weekendPrice', 'weekPrice'] })} /> Price on request
          </label>
          <div className={styles.formGrid}>
            {(['midweekPrice', 'weekendPrice', 'weekPrice'] as const).map((f) => (
              <Field key={f} label={f === 'midweekPrice' ? 'Mid-week price' : f === 'weekendPrice' ? 'Weekend price' : 'Week price'} htmlFor={id(f)} error={errors[f]?.message}>
                <input id={id(f)} type="number" min={0} step="0.01" className={inputClass} disabled={priceOnRequest}
                  {...register(f, {
                    setValueAs: numberOrNull,
                    validate: {
                      required: (v, form) => form.priceOnRequest || (v !== null && !Number.isNaN(v)) || 'Enter a price, or tick Price on request.',
                      min: rules.number({ min: 0, message: "Price can't be negative." }),
                    },
                  })} />
              </Field>
            ))}
          </div>
          <label className={styles.checkLabel}><input type="checkbox" {...register('quantityApplies')} /> Customers choose a quantity</label>
          <div className={styles.formGrid}>
            <Field label="Capacity" htmlFor={id('cap')} error={errors.capacity?.message} hint="Optional. Shared across all boats and bookings.">
              <input id={id('cap')} type="number" min={0} className={inputClass}
                {...register('capacity', { setValueAs: numberOrNull, validate: rules.number({ min: 0, integer: true, message: "Capacity can't be negative." }) })} />
            </Field>
            <Field label="Capacity unit label" htmlFor={id('unit')} error={errors.capacityUnitLabel?.message} hint='e.g. "car spots"'>
              <input id={id('unit')} className={inputClass} disabled={noCapacity} {...register('capacityUnitLabel', { validate: rules.maxLength(40, 'Capacity unit label') })} />
            </Field>
          </div>
        </fieldset>
      </form>
    </Dialog>
  );
}
