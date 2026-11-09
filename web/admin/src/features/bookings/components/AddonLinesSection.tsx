import { useId, useState } from 'react';
import { useFieldArray, useFormContext, useWatch } from 'react-hook-form';
import { useMutation, useQueries } from '@tanstack/react-query';
import { Button } from '@shared/components/Button/Button';
import { Field, inputClass } from '@shared/components/Field/Field';
import { errorMessage } from '@shared/lib/http';
import { formatMoney } from '@shared/utils/money';
import { api, keys } from '@/lib/api';
import type { Addon } from '@/lib/types';
import { lineAmount, settingsPrice } from '../utils/bookingForm';
import type { BookingFormValues } from './BookingDialog';
import styles from './BookingDialog.module.css';

const numberOrNull = (v: unknown) => (v === '' || v === null || v === undefined ? null : Number(v));

/**
 * Hire price and the booking's add-on lines. Adding, editing and removing lines only changes the form: nothing is saved
 * until the booking is.
 */
export function AddonLinesSection({ addons, bookingId, isNew, ids, errorHirePrice }: {
  addons: Addon[];
  bookingId: string | null;
  isNew: boolean;
  ids: { hirePrice: string };
  errorHirePrice?: string;
}) {
  const { control, register, getValues, setValue, formState: { errors } } = useFormContext<BookingFormValues>();
  const { fields, append, remove } = useFieldArray({ control, name: 'addonLines', keyName: 'key' });
  const lines = useWatch({ control, name: 'addonLines' }) ?? [];
  const [startDate, endDate, periodType] = useWatch({ control, name: ['startDate', 'endDate', 'periodType'] });
  const active = addons.filter((a) => a.isActive);
  const [picked, setPicked] = useState(active[0]?.id ?? '');
  const pickerId = useId();

  // Remaining capacity for the dates (excluding this booking), for the picked add-on and every capacity-limited line.
  const capacityIds = Array.from(new Set([picked, ...lines.map((l) => l.addonId)])).filter((id) => addons.find((a) => a.id === id)?.capacity != null);
  const datesValid = !!startDate && !!endDate && endDate > startDate;
  const remaining = useQueries({
    queries: capacityIds.map((id) => ({
      queryKey: keys.addonRemaining(id, startDate, endDate, bookingId),
      queryFn: () => api.addonRemaining(id, startDate, endDate, bookingId ?? undefined),
      enabled: datesValid,
      staleTime: 30_000,
    })),
  });
  const remainingFor = (id: string) => remaining[capacityIds.indexOf(id)]?.data?.remaining;

  const add = () => {
    const addon = addons.find((a) => a.id === picked);
    if (!addon) return;
    append({ id: null, addonId: addon.id, name: addon.name, quantity: 1, unitPrice: settingsPrice(addon, getValues('periodType')) });
  };

  const recalc = useMutation({
    mutationFn: () => api.quote({
      boatId: getValues('boatId'), periodType: getValues('periodType'), startDate: getValues('startDate'),
      addonIds: getValues('addonLines').map((l) => l.addonId),
    }),
    onSuccess: (q) => {
      if (q.hirePrice !== null) setValue('hirePrice', q.hirePrice, { shouldDirty: true });
      getValues('addonLines').forEach((l, i) => {
        const price = q.addonPrices.find((p) => p.addonId === l.addonId)?.unitPrice;
        if (price !== null && price !== undefined) setValue(`addonLines.${i}.unitPrice`, price, { shouldDirty: true });
      });
    },
  });

  return (
    <div className={styles.addons}>
      <div className={styles.hireRow}>
        <Field label="Hire price" htmlFor={ids.hirePrice} error={errorHirePrice}
          hint={periodType === 'Custom' ? 'Custom stays: type the price.' : isNew ? 'Pre-filled from the rate table.' : undefined}>
          <input id={ids.hirePrice} type="number" min={0} step="0.01" className={inputClass} {...register('hirePrice', { setValueAs: numberOrNull })} />
        </Field>
        <Button onClick={() => recalc.mutate()} busy={recalc.isPending} busyLabel="Recalculating…" disabled={!getValues('boatId')}>
          Recalculate from rates
        </Button>
        {recalc.isError && <span role="alert" className={styles.inlineError}>{errorMessage(recalc.error)}</span>}
      </div>

      <h3 className={styles.subheading}>Add-ons</h3>
      {fields.length === 0 && <p className={styles.muted}>No add-ons.</p>}
      <ul className={styles.lines}>
        {fields.map((f, i) => {
          const line = lines[i] ?? f;
          const amount = lineAmount(line);
          const left = remainingFor(line.addonId);
          const totalQty = lines.filter((l) => l.addonId === line.addonId).reduce((s, l) => s + (Number(l.quantity) || 0), 0);
          const unit = addons.find((a) => a.id === line.addonId)?.capacityUnitLabel;
          return (
            <li key={f.key} className={styles.line} data-testid={`line-${i}`}>
              <div className={styles.lineName}>{line.name}</div>
              <Field label="Quantity" htmlFor={`${pickerId}-q-${i}`} error={errors.addonLines?.[i]?.quantity?.message}>
                <input id={`${pickerId}-q-${i}`} type="number" min={1} className={inputClass} {...register(`addonLines.${i}.quantity`, { setValueAs: numberOrNull })} />
              </Field>
              <Field label="Unit price" htmlFor={`${pickerId}-p-${i}`} error={errors.addonLines?.[i]?.unitPrice?.message}>
                <input id={`${pickerId}-p-${i}`} type="number" min={0} step="0.01" className={inputClass} placeholder="Price TBC"
                  {...register(`addonLines.${i}.unitPrice`, { setValueAs: numberOrNull })} />
              </Field>
              <div className={styles.lineTotal}>
                <span className={styles.totalLabel}>Total</span>
                <span data-testid={`line-total-${i}`}>{amount === null ? 'Price TBC' : formatMoney(amount)}</span>
              </div>
              <Button variant="danger" size="sm" onClick={() => remove(i)} aria-label={`Remove ${line.name}`}>Remove</Button>
              {left !== undefined && (
                <div className={styles.capacity}>
                  {left} {unit ?? 'available'} remaining for these dates
                  {totalQty > left && <strong className={styles.warn}> — exceeds capacity</strong>}
                </div>
              )}
            </li>
          );
        })}
      </ul>
      <div className={styles.picker}>
        <label htmlFor={pickerId} className="visually-hidden">Add-on to add</label>
        <select id={pickerId} className={inputClass} value={picked} onChange={(e) => setPicked(e.target.value)}>
          {active.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
        </select>
        <Button onClick={add} disabled={!picked}>Add</Button>
        {remainingFor(picked) !== undefined && <span className={styles.muted}>{remainingFor(picked)} remaining for these dates</span>}
      </div>
    </div>
  );
}
