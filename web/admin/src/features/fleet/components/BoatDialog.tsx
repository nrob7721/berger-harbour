import { useId, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Button } from '@shared/components/Button/Button';
import { Dialog } from '@shared/components/Dialog/Dialog';
import { Field, inputClass } from '@shared/components/Field/Field';
import { useToast } from '@shared/components/Toast/Toast';
import { applyServerErrors } from '@shared/lib/forms';
import { ApiError, errorMessage } from '@shared/lib/http';
import { formatMoney } from '@shared/utils/money';
import { useAddons, useBusinessSettings, useSeasons } from '@/hooks/useReferenceData';
import { api, keys } from '@/lib/api';
import type { Boat, PeriodType, SaveBoatRequest } from '@/lib/types';
import { BoatThumb } from './BoatThumb';
import { UnavailabilitiesTab } from './UnavailabilitiesTab';
import styles from './BoatDialog.module.css';

const RATE_PERIODS: { type: PeriodType; label: string }[] = [
  { type: 'Midweek', label: 'Mid-week' },
  { type: 'Weekend', label: 'Weekend' },
  { type: 'Week', label: 'Week' },
  { type: 'LongWeekend', label: 'Long weekend' },
];

type Tab = 'details' | 'rates' | 'addons' | 'unavailabilities';

interface BoatForm {
  name: string;
  slug: string;
  berth: number | null;
  adultBeds: number | null;
  kidsBeds: number | null;
  beddingDescription: string;
  securityBond: number | null;
  depositAmount: number | null;
  imageUrl: string;
  hasSpa: boolean;
  isPetFriendly: boolean;
  isActive: boolean;
  rates: Record<string, Partial<Record<PeriodType, number | null>>>;
  allowedAddonIds: string[];
}

const numberOrNull = (v: unknown) => (v === '' || v === null || v === undefined ? null : Number(v));
const FIELDS = ['name', 'slug', 'berth', 'adultBeds', 'kidsBeds', 'beddingDescription', 'securityBond', 'depositAmount', 'imageUrl', 'isActive', 'rates'] as const;

function defaults(boat: Boat | null): BoatForm {
  const rates: BoatForm['rates'] = {};
  for (const r of boat?.rates ?? []) (rates[r.seasonId] ??= {})[r.periodType] = r.price;
  return {
    name: boat?.name ?? '', slug: boat?.slug ?? '', berth: boat?.berth ?? null, adultBeds: boat?.adultBeds ?? null, kidsBeds: boat?.kidsBeds ?? null,
    beddingDescription: boat?.beddingDescription ?? '', securityBond: boat?.securityBond ?? 0, depositAmount: boat?.depositAmount ?? null,
    imageUrl: boat?.imageUrl ?? '', hasSpa: boat?.hasSpa ?? false, isPetFriendly: boat?.isPetFriendly ?? false, isActive: boat?.isActive ?? false,
    rates, allowedAddonIds: boat?.allowedAddonIds ?? [],
  };
}

export function BoatDialog({ boat, onClose }: { boat: Boat | null; onClose: () => void }) {
  const [tab, setTab] = useState<Tab>('details');
  const queryClient = useQueryClient();
  const toast = useToast();
  const seasons = useSeasons();
  const addons = useAddons();
  const settings = useBusinessSettings();
  const base = useId();
  const id = (f: string) => `${base}-${f}`;
  const [formError, setFormError] = useState<string | null>(null);
  const { register, handleSubmit, setError, control, formState: { errors } } = useForm<BoatForm>({ defaultValues: defaults(boat) });
  const imageUrl = useWatch({ control, name: 'imageUrl' });

  const save = useMutation({
    mutationFn: (v: BoatForm) => {
      const body: SaveBoatRequest = {
        name: v.name, slug: v.slug.trim(), berth: v.berth ?? 0, adultBeds: v.adultBeds, kidsBeds: v.kidsBeds,
        beddingDescription: v.beddingDescription || null, securityBond: v.securityBond ?? 0, depositAmount: v.depositAmount,
        imageUrl: v.imageUrl.trim() || null, hasSpa: v.hasSpa, isPetFriendly: v.isPetFriendly, isActive: v.isActive,
        rates: Object.entries(v.rates).flatMap(([seasonId, byType]) =>
          Object.entries(byType ?? {}).filter(([, price]) => price !== null && price !== undefined && !Number.isNaN(price))
            .map(([periodType, price]) => ({ seasonId, periodType: periodType as PeriodType, price: price as number }))),
        allowedAddonIds: v.allowedAddonIds,
        version: boat?.version ?? 0,
      };
      return boat ? api.updateBoat(boat.id, body) : api.createBoat(body);
    },
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: keys.boats });
      void queryClient.invalidateQueries({ queryKey: keys.timelineRoot });
      toast.show(`${saved.name} saved.`);
      onClose();
    },
    onError: (e) => {
      const unmatched = applyServerErrors(e, setError, FIELDS);
      if (e instanceof ApiError && (e.errors.berth || e.errors.slug || e.errors.name || e.errors.imageUrl || e.errors.depositAmount)) setTab('details');
      setFormError(unmatched[0] ?? (e instanceof ApiError && Object.keys(e.errors).length ? null : errorMessage(e)));
    },
  });
  const saving = save.isPending;
  const submit = handleSubmit((v) => { setFormError(null); save.mutate(v); });

  const tabs: { id: Tab; label: string }[] = [
    { id: 'details', label: 'Details' }, { id: 'rates', label: 'Rates' }, { id: 'addons', label: 'Add-ons' },
    ...(boat ? [{ id: 'unavailabilities' as const, label: 'Unavailabilities' }] : []),
  ];

  return (
    <Dialog open title={boat ? boat.name : 'New boat'} onClose={onClose} locked={saving} size="lg"
      footer={tab === 'unavailabilities' ? <Button onClick={onClose}>Close</Button> : <>
        {formError && <span role="alert" className={styles.error}>{formError}</span>}
        {errors.isActive?.message && <span role="alert" className={styles.error}>{errors.isActive.message}</span>}
        <Button onClick={onClose} disabled={saving}>Cancel</Button>
        <Button variant="primary" onClick={submit} busy={saving} busyLabel="Saving…">Save</Button>
      </>}>
      <div role="tablist" className={styles.tabs} aria-label="Boat">
        {tabs.map((t) => (
          <button key={t.id} type="button" role="tab" id={id(`tab-${t.id}`)} aria-selected={tab === t.id} aria-controls={id(`panel-${t.id}`)}
            className={tab === t.id ? styles.activeTab : ''} onClick={() => setTab(t.id)}>
            {t.label}
          </button>
        ))}
      </div>
      <form onSubmit={submit} noValidate>
        <fieldset className={styles.fieldset} disabled={saving}>
          <div role="tabpanel" id={id('panel-details')} aria-labelledby={id('tab-details')} hidden={tab !== 'details'} className={styles.grid}>
            <Field label="Name" htmlFor={id('name')} error={errors.name?.message}>
              <input id={id('name')} className={inputClass} {...register('name')} />
            </Field>
            <Field label="Slug" htmlFor={id('slug')} error={errors.slug?.message} hint="Must match the WordPress page, e.g. pacific-blue">
              <input id={id('slug')} className={inputClass} {...register('slug')} />
            </Field>
            <Field label="Berth" htmlFor={id('berth')} error={errors.berth?.message} hint="How many people the boat sleeps">
              <input id={id('berth')} type="number" min={1} className={inputClass} {...register('berth', { setValueAs: numberOrNull })} />
            </Field>
            <Field label="Number of adult beds" htmlFor={id('adultBeds')} error={errors.adultBeds?.message} hint="A double or queen counts as one bed">
              <input id={id('adultBeds')} type="number" min={0} className={inputClass} {...register('adultBeds', { setValueAs: numberOrNull })} />
            </Field>
            <Field label="Number of kids beds" htmlFor={id('kidsBeds')} error={errors.kidsBeds?.message}>
              <input id={id('kidsBeds')} type="number" min={0} className={inputClass} {...register('kidsBeds', { setValueAs: numberOrNull })} />
            </Field>
            <Field label="Security bond" htmlFor={id('securityBond')} error={errors.securityBond?.message}>
              <input id={id('securityBond')} type="number" min={0} step="0.01" className={inputClass} {...register('securityBond', { setValueAs: numberOrNull })} />
            </Field>
            <Field label="Deposit" htmlFor={id('depositAmount')} error={errors.depositAmount?.message}
              hint={`Leave empty to use the default deposit of ${formatMoney(settings.data?.depositAmount ?? 1000)} from Settings`}>
              <input id={id('depositAmount')} type="number" min={0} step="0.01" className={inputClass} {...register('depositAmount', { setValueAs: numberOrNull })} />
            </Field>
            <Field className={styles.wide} label="Bedding description" htmlFor={id('bedding')} error={errors.beddingDescription?.message}>
              <textarea id={id('bedding')} className={inputClass} rows={2} {...register('beddingDescription')} />
            </Field>
            <Field className={styles.wide} label="Image URL" htmlFor={id('imageUrl')} error={errors.imageUrl?.message} hint="Link to an image on bergerhouseboats.com.au">
              <input id={id('imageUrl')} type="url" className={inputClass} {...register('imageUrl')} />
            </Field>
            {imageUrl && <BoatThumb src={imageUrl} alt="Image preview" className={styles.preview} />}
            <div className={`${styles.checks} ${styles.wide}`}>
              <label><input type="checkbox" {...register('hasSpa')} /> Spa</label>
              <label><input type="checkbox" {...register('isPetFriendly')} /> Pet friendly</label>
              <label className={styles.toggle}><input type="checkbox" role="switch" {...register('isActive')} /> Active (bookable online and by staff)</label>
            </div>
          </div>

          <div role="tabpanel" id={id('panel-rates')} aria-labelledby={id('tab-rates')} hidden={tab !== 'rates'}>
            <p className={styles.muted}>Prices in AUD including GST. Mid-week, Weekend and Week are needed in every season before a boat can be activated.</p>
            <table className={styles.rates}>
              <thead><tr><th>Season</th>{RATE_PERIODS.map((p) => <th key={p.type}>{p.label}</th>)}</tr></thead>
              <tbody>
                {seasons.data?.map((s) => (
                  <tr key={s.id}>
                    <th scope="row">{s.name}</th>
                    {RATE_PERIODS.map((p) => (
                      <td key={p.type}>
                        <input type="number" min={0} step="0.01" aria-label={`${s.name} ${p.label} rate`} className={inputClass}
                          {...register(`rates.${s.id}.${p.type}`, { setValueAs: numberOrNull })} />
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div role="tabpanel" id={id('panel-addons')} aria-labelledby={id('tab-addons')} hidden={tab !== 'addons'}>
            <p className={styles.muted}>Add-ons offered for this boat.</p>
            <ul className={styles.addonList}>
              {addons.data?.map((a) => (
                <li key={a.id}>
                  <label><input type="checkbox" value={a.id} {...register('allowedAddonIds')} /> {a.name}{a.isActive ? '' : ' (inactive)'}</label>
                </li>
              ))}
            </ul>
          </div>
        </fieldset>
      </form>
      {boat && tab === 'unavailabilities' && (
        <div role="tabpanel" id={id('panel-unavailabilities')} aria-labelledby={id('tab-unavailabilities')}>
          <UnavailabilitiesTab boat={boat} />
        </div>
      )}
    </Dialog>
  );
}
