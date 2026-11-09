import { useId, useState } from 'react';
import { useWatch, type FieldErrors } from 'react-hook-form';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Button } from '@shared/components/Button/Button';
import { Dialog } from '@shared/components/Dialog/Dialog';
import { Field, inputClass } from '@shared/components/Field/Field';
import { useToast } from '@shared/components/Toast/Toast';
import { applyServerErrors, rules, useAppForm } from '@shared/lib/forms';
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
const DETAILS_FIELDS = FIELDS.filter((f) => f !== 'rates' && f !== 'isActive');

const SLUG = /^[a-z0-9]+(-[a-z0-9]+)*$/;
const IMAGE_HOSTS = ['bergerhouseboats.com.au', 'www.bergerhouseboats.com.au'];

/** The server's image URL rule: empty, or an absolute https:// URL on the WordPress site. */
function isValidImageUrl(value: string): boolean {
  const v = value.trim();
  if (!v) return true;
  try {
    const url = new URL(v);
    return url.protocol === 'https:' && IMAGE_HOSTS.includes(url.hostname);
  } catch {
    return false;
  }
}

/** Berth ≥ adult beds + kids beds; the error belongs to Berth. */
const berthCoversBeds = (berth: number | null, form: BoatForm) => {
  const beds = (Number(form.adultBeds) || 0) + (Number(form.kidsBeds) || 0);
  return berth === null || Number.isNaN(berth) || berth < 1 || berth >= beds || `Berth can't be less than adult beds + kids beds (${beds}).`;
};

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
  // Each field is validated when it loses focus (§6.1).
  const { register, handleSubmit, setError, setFocus, control, formState: { errors } } = useAppForm<BoatForm>({ defaultValues: defaults(boat) });
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
  /** The first invalid field may be on a hidden tab: show that tab, then focus the field. */
  const showFirstInvalid = (errs: FieldErrors<BoatForm>) => {
    const detailsField = DETAILS_FIELDS.find((f) => errs[f]);
    if (detailsField) {
      setTab('details');
      setTimeout(() => setFocus(detailsField));
      return;
    }
    const rate = Object.entries(errs.rates ?? {}).flatMap(([seasonId, byType]) =>
      Object.keys((byType ?? {}) as object).map((type) => `rates.${seasonId}.${type}` as const))[0];
    if (rate) {
      setTab('rates');
      setTimeout(() => setFocus(rate as `rates.${string}`));
    }
  };
  const submit = handleSubmit((v) => { setFormError(null); save.mutate(v); }, showFirstInvalid);
  // A server error on `rates` as a whole, or the first rate field's own error.
  const rateErrors = Object.values(errors.rates ?? {}).flatMap((byType) => Object.values((byType ?? {}) as Record<string, { message?: unknown }>));
  const rateError = [(errors.rates as { message?: unknown } | undefined)?.message, ...rateErrors.map((e) => e?.message)]
    .find((m): m is string => typeof m === 'string' && m.length > 0);

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
              <input id={id('name')} className={inputClass} {...register('name', { validate: { required: rules.required('Name is required.'), max: rules.maxLength(80, 'Name') } })} />
            </Field>
            <Field label="Slug" htmlFor={id('slug')} error={errors.slug?.message} hint="Must match the WordPress page, e.g. pacific-blue">
              <input id={id('slug')} className={inputClass} {...register('slug', {
                validate: {
                  required: rules.required('Slug is required.'),
                  format: (v) => !v.trim() || (v.trim().length <= 80 && SLUG.test(v.trim())) || 'Use lower-case letters, numbers and hyphens, e.g. pacific-blue.',
                },
              })} />
            </Field>
            <Field label="Berth" htmlFor={id('berth')} error={errors.berth?.message} hint="How many people the boat sleeps">
              <input id={id('berth')} type="number" min={1} className={inputClass} {...register('berth', {
                setValueAs: numberOrNull,
                validate: {
                  min: (v) => (v !== null && !Number.isNaN(v) && Number.isInteger(v) && v >= 1) || 'Berth must be at least 1.',
                  beds: berthCoversBeds,
                },
              })} />
            </Field>
            <Field label="Number of adult beds" htmlFor={id('adultBeds')} error={errors.adultBeds?.message} hint="A double or queen counts as one bed">
              <input id={id('adultBeds')} type="number" min={0} className={inputClass} {...register('adultBeds', {
                setValueAs: numberOrNull, deps: 'berth', validate: rules.number({ min: 0, integer: true, message: "Adult beds can't be negative." }),
              })} />
            </Field>
            <Field label="Number of kids beds" htmlFor={id('kidsBeds')} error={errors.kidsBeds?.message}>
              <input id={id('kidsBeds')} type="number" min={0} className={inputClass} {...register('kidsBeds', {
                setValueAs: numberOrNull, deps: 'berth', validate: rules.number({ min: 0, integer: true, message: "Kids beds can't be negative." }),
              })} />
            </Field>
            <Field label="Security bond" htmlFor={id('securityBond')} error={errors.securityBond?.message}>
              <input id={id('securityBond')} type="number" min={0} step="0.01" className={inputClass} {...register('securityBond', {
                setValueAs: numberOrNull, validate: rules.number({ min: 0, message: "Security bond can't be negative." }),
              })} />
            </Field>
            <Field label="Deposit" htmlFor={id('depositAmount')} error={errors.depositAmount?.message}
              hint={`Leave empty to use the default deposit of ${formatMoney(settings.data?.depositAmount ?? 1000)} from Settings`}>
              <input id={id('depositAmount')} type="number" min={0} step="0.01" className={inputClass} {...register('depositAmount', {
                setValueAs: numberOrNull,
                validate: rules.number({ min: 0, minExclusive: true, message: 'Deposit must be more than $0, or left empty to use the default.' }),
              })} />
            </Field>
            <Field className={styles.wide} label="Bedding description" htmlFor={id('bedding')} error={errors.beddingDescription?.message}>
              <textarea id={id('bedding')} className={inputClass} rows={2} {...register('beddingDescription', { validate: rules.maxLength(500, 'Bedding description') })} />
            </Field>
            <Field className={styles.wide} label="Image URL" htmlFor={id('imageUrl')} error={errors.imageUrl?.message} hint="Link to an image on bergerhouseboats.com.au">
              <input id={id('imageUrl')} type="url" className={inputClass}
                {...register('imageUrl', { validate: (v) => isValidImageUrl(v) || 'Use an https:// link to an image on bergerhouseboats.com.au.' })} />
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
                          aria-invalid={!!errors.rates?.[s.id]?.[p.type] || undefined}
                          {...register(`rates.${s.id}.${p.type}`, { setValueAs: numberOrNull, validate: rules.number({ min: 0, message: "Rates can't be negative." }) })} />
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
            {rateError && <div role="alert" className={styles.error}>{rateError}</div>}
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
