import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { Controller, FormProvider, useFormContext, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';
import { Button } from '@shared/components/Button/Button';
import { Dialog } from '@shared/components/Dialog/Dialog';
import { Field, inputClass } from '@shared/components/Field/Field';
import { MobileInput } from '@shared/components/MobileInput/MobileInput';
import { Spinner } from '@shared/components/Spinner/Spinner';
import { useToast } from '@shared/components/Toast/Toast';
import { applyServerErrors, useAppForm } from '@shared/lib/forms';
import { ApiError, errorMessage } from '@shared/lib/http';
import { AUSTRALIA, AUSTRALIAN_STATES, countryOptions } from '@shared/utils/countries';
import { addDays, formatTimestamp, todayInSydney, type IsoDate } from '@shared/utils/dates';
import { EMAIL_ERROR, isValidEmail, normaliseEmail } from '@shared/utils/email';
import { formatMoney } from '@shared/utils/money';
import { isValidMobile, MOBILE_ERROR } from '@shared/utils/phone';
import { Badge } from '@/components/Badge/Badge';
import { useAddons, useBoats } from '@/hooks/useReferenceData';
import { api, keys } from '@/lib/api';
import type { BookingDetail, CustomerLookup } from '@/lib/types';
import { addressSchema, toAddressForm, toAddressRequest, type AddressForm } from '@/utils/address';
import { changeoverWarnings, defaultEndDate, guestWarnings, newBookingPeriod, PERIOD_TYPES, type LineForm } from '../utils/bookingForm';
import { AddonLinesSection } from './AddonLinesSection';
import { PaymentsSection } from './PaymentsSection';
import { PriceBreakdown } from './PriceBreakdown';
import styles from './BookingDialog.module.css';

const optionalNumber = z.number().nullable();

const schema = z.object({
  boatId: z.string().min(1, 'Choose a boat.'),
  periodType: z.enum(['Midweek', 'Weekend', 'Week', 'LongWeekend', 'Custom']),
  startDate: z.string().min(1, 'Enter the start date.'),
  endDate: z.string().min(1, 'Enter the end date.'),
  numberOfAdults: z.number({ message: 'Enter the number of adults.' }).int().min(1, 'At least 1 adult is required.'),
  numberOfChildren: z.number({ message: 'Enter the number of children.' }).int().min(0, "Children can't be negative."),
  checkInTime: z.string().regex(/^\d{2}:\d{2}$/, 'Enter a time.'),
  checkOutTime: z.string().regex(/^\d{2}:\d{2}$/, 'Enter a time.'),
  isStandby: z.boolean(),
  bookingComments: z.string(),
  customerComments: z.string(),
  hirePrice: optionalNumber.refine((v) => v !== null && v >= 0, 'Enter the hire price.'),
  status: z.enum(['PendingPayment', 'Active', 'Superseded', 'Cancelled', 'Expired']),
  email: z.string().trim().min(1, 'Email is required.').refine(isValidEmail, EMAIL_ERROR),
  firstName: z.string().trim().min(1, 'First name is required.'),
  lastName: z.string().trim().min(1, 'Last name is required.'),
  mobile: z.string().refine((v) => !v || isValidMobile(v), MOBILE_ERROR),
  secondaryMobile: z.string().refine((v) => !v || isValidMobile(v), MOBILE_ERROR),
  address: addressSchema,
  addonLines: z.array(z.object({
    id: z.string().nullable(), addonId: z.string(), name: z.string(),
    quantity: z.number({ message: 'Enter a quantity.' }).int().min(1, 'At least 1.'), unitPrice: optionalNumber.refine((v) => v === null || v >= 0, "Can't be negative."),
  })),
}).refine((v) => !v.startDate || !v.endDate || v.endDate > v.startDate, { path: ['endDate'], message: 'The end date must be after the start date.' });

export type BookingFormValues = z.infer<typeof schema> & { addonLines: LineForm[]; address: AddressForm };

const FIELDS = ['boatId', 'periodType', 'startDate', 'endDate', 'numberOfAdults', 'numberOfChildren', 'checkInTime', 'checkOutTime', 'hirePrice',
  'status', 'email', 'firstName', 'lastName', 'mobile', 'secondaryMobile', 'address.line1', 'address.line2', 'address.locality', 'address.state',
  'address.postcode', 'address.country', 'bookingComments', 'customerComments', 'addonLines'] as const;

export interface BookingDialogProps {
  /**
   * Edit an existing booking, or create one. A new booking's period is pre-filled from `day` (today when absent), and its boat
   * from `boatId` (empty when absent).
   */
  bookingId: string | null;
  initial?: { boatId?: string; day?: IsoDate };
  onClose: () => void;
}

export function BookingDialog({ bookingId, initial, onClose }: BookingDialogProps) {
  const detail = useQuery({ queryKey: keys.booking(bookingId ?? ''), queryFn: () => api.booking(bookingId!), enabled: !!bookingId, staleTime: 0 });
  const title = bookingId ? (detail.data ? `Booking ${detail.data.reference ?? '(pending payment)'}` : 'Booking') : 'New booking';
  if (bookingId && !detail.data) {
    return (
      <Dialog open title={title} onClose={onClose} size="xl">
        {detail.isError ? <p role="alert">{errorMessage(detail.error)}</p> : <p><Spinner /> Loading…</p>}
      </Dialog>
    );
  }
  // A confirmed hold reloads as an ordinary Active booking: a new status remounts the form with the new Customer.
  return <BookingForm key={bookingId ? `${bookingId}:${detail.data?.status}` : 'new'} detail={detail.data ?? null} initial={initial} onClose={onClose} title={title} />;
}

function defaults(detail: BookingDetail | null, initial: BookingDialogProps['initial']): BookingFormValues {
  if (detail) {
    const c = detail.customer;
    const snapshot = detail.customerDetails;
    return {
      boatId: detail.boatId, periodType: detail.periodType, startDate: detail.startDate, endDate: detail.endDate,
      numberOfAdults: detail.numberOfAdults, numberOfChildren: detail.numberOfChildren, checkInTime: detail.checkInTime, checkOutTime: detail.checkOutTime,
      isStandby: detail.isStandby, bookingComments: detail.bookingComments ?? '', customerComments: c?.comments ?? '', hirePrice: detail.hirePrice,
      status: detail.status, email: c?.email ?? snapshot?.email ?? '', firstName: c?.firstName ?? snapshot?.firstName ?? '',
      lastName: c?.lastName ?? snapshot?.lastName ?? '', mobile: c?.mobile ?? snapshot?.mobileNumber ?? '', secondaryMobile: c?.secondaryMobile ?? '',
      address: toAddressForm(c?.address),
      addonLines: detail.addonLines.map((l) => ({ id: l.id, addonId: l.addonId, name: l.name, quantity: l.quantity, unitPrice: l.unitPrice })),
    };
  }
  const period = newBookingPeriod(initial?.day ?? todayInSydney());
  return {
    boatId: initial?.boatId ?? '', ...period,
    numberOfAdults: 2, numberOfChildren: 0, checkInTime: '11:00', checkOutTime: '08:00', isStandby: false, bookingComments: '', customerComments: '',
    hirePrice: null, status: 'Active', email: '', firstName: '', lastName: '', mobile: '', secondaryMobile: '', address: toAddressForm(null), addonLines: [],
  };
}

const numberOrNull = (v: unknown) => (v === '' || v === null || v === undefined ? null : Number(v));

function BookingForm({ detail, initial, onClose, title }: { detail: BookingDetail | null; initial: BookingDialogProps['initial']; onClose: () => void; title: string }) {
  const isNew = !detail;
  const queryClient = useQueryClient();
  const toast = useToast();
  const boats = useBoats();
  const addons = useAddons();
  const baseId = useId();
  const ids = Object.fromEntries(FIELDS.map((f) => [f, `${baseId}-${f}`])) as Record<(typeof FIELDS)[number], string>;
  const [formError, setFormError] = useState<string | null>(null);
  const [lookup, setLookup] = useState<CustomerLookup | null>(null);
  const lastLookup = useRef<string>((detail?.customer?.email ?? '').toLowerCase());

  // Each field is validated when it loses focus (§6.1).
  const form = useAppForm<BookingFormValues>({ resolver: zodResolver(schema) as never, defaultValues: defaults(detail, initial) });
  const { register, control, setValue, getValues, handleSubmit, setError, formState: { errors, isDirty } } = form;
  const values = useWatch({ control }) as BookingFormValues;
  const boat = boats.data?.find((b) => b.id === values.boatId);
  /** PendingPayment and Expired holds are read-only (decision 36). */
  const isHold = detail?.status === 'PendingPayment' || detail?.status === 'Expired';

  // The boat <select> is registered before its options load; re-apply the pre-filled boat once they exist.
  useEffect(() => {
    if (boats.data) setValue('boatId', getValues('boatId'));
  }, [boats.data]); // eslint-disable-line react-hooks/exhaustive-deps

  // New bookings: pre-fill the hire price from the rate table while it is still empty.
  const prefill = useQuery({
    queryKey: ['prefill', values.boatId, values.periodType, values.startDate],
    queryFn: () => api.quote({ boatId: values.boatId, periodType: values.periodType, startDate: values.startDate, addonIds: [] }),
    enabled: isNew && !!values.boatId && !!values.startDate && values.periodType !== 'Custom',
  });
  useEffect(() => {
    if (isNew && prefill.data?.hirePrice !== undefined && prefill.data.hirePrice !== null && getValues('hirePrice') === null)
      setValue('hirePrice', prefill.data.hirePrice, { shouldDirty: true });
  }, [prefill.data]); // eslint-disable-line react-hooks/exhaustive-deps

  // Same-day changeover clashes only matter with custom times.
  const customTimes = values.checkInTime !== '11:00' || values.checkOutTime !== '08:00';
  const nearby = useQuery({
    queryKey: keys.timeline(addDays(values.startDate || '2000-01-01', -1), addDays(values.endDate || '2000-01-02', 1), false),
    queryFn: () => api.timeline(addDays(values.startDate, -1), addDays(values.endDate, 1), false),
    enabled: customTimes && !!values.boatId && !!values.startDate && !!values.endDate && values.endDate > values.startDate,
    staleTime: 30_000,
  });
  const warnings = [
    ...guestWarnings(boat, values.numberOfAdults, values.numberOfChildren),
    ...(customTimes && nearby.data
      ? changeoverWarnings(nearby.data.bookings, values.boatId, detail?.id ?? null, values.startDate, values.endDate, values.checkInTime, values.checkOutTime)
      : []),
  ];

  /** On blur, after the email is validated: an invalid email shows its error and is never looked up. */
  const lookupEmail = async () => {
    const email = normaliseEmail(getValues('email'));
    if (!isValidEmail(email) || email === lastLookup.current || isHold) return;
    lastLookup.current = email;
    try {
      const found = await api.customerByEmail(email);
      setLookup(found);
      setValue('firstName', found.customer.firstName, { shouldDirty: true });
      setValue('lastName', found.customer.lastName, { shouldDirty: true });
      setValue('mobile', found.customer.mobile ?? '', { shouldDirty: true });
      setValue('secondaryMobile', found.customer.secondaryMobile ?? '', { shouldDirty: true });
      setValue('address', toAddressForm(found.customer.address), { shouldDirty: true });
      setValue('customerComments', found.customer.comments ?? '', { shouldDirty: true });
    } catch (e) {
      setLookup(null);
      if (!(e instanceof ApiError && e.status === 404)) setFormError(errorMessage(e));
    }
  };
  const frequentFloater = isNew ? lookup?.frequentFloater : (lookup?.frequentFloater ?? detail?.frequentFloater);

  const save = useMutation({
    mutationFn: (v: BookingFormValues) => {
      const common = {
        boatId: v.boatId, periodType: v.periodType, startDate: v.startDate, endDate: v.endDate, numberOfAdults: v.numberOfAdults,
        numberOfChildren: v.numberOfChildren, checkInTime: v.checkInTime, checkOutTime: v.checkOutTime, isStandby: v.isStandby,
        bookingComments: v.bookingComments || null, customerComments: v.customerComments || null, hirePrice: v.hirePrice,
        email: v.email.trim(), firstName: v.firstName.trim(), lastName: v.lastName.trim(), mobile: v.mobile || null, secondaryMobile: v.secondaryMobile || null,
        address: toAddressRequest(v.address),
        addonLines: v.addonLines.map((l) => ({ id: l.id, addonId: l.addonId, quantity: l.quantity, unitPrice: l.unitPrice })),
      };
      return isNew ? api.createBooking(common) : api.updateBooking(detail!.id, { ...common, status: v.status, version: detail!.version });
    },
    onSuccess: (result) => {
      void queryClient.invalidateQueries({ queryKey: keys.timelineRoot });
      void queryClient.invalidateQueries({ queryKey: keys.customers });
      queryClient.removeQueries({ queryKey: keys.booking(result.booking.id) });
      const note = result.warnings.length > 0 ? ` Note: ${result.warnings.join(' ')}` : '';
      toast.show(`${isNew ? 'Booking created' : 'Changes saved'} (${result.booking.reference ?? 'pending payment'}).${note}`);
      onClose();
    },
    onError: (e) => {
      const unmatched = applyServerErrors(e, setError, FIELDS, { boatId: 'boatId', hirePrice: 'hirePrice' });
      const mapped = e instanceof ApiError && Object.keys(e.errors).length > 0 && unmatched.length === 0;
      setFormError(mapped ? 'Please fix the highlighted fields.' : (unmatched[0] ?? errorMessage(e)));
    },
  });
  const saving = save.isPending;
  const submit = handleSubmit((v) => { setFormError(null); save.mutate(v); }, () => setFormError('Please fix the highlighted fields.'));

  const onPeriodTypeChange = (periodType: BookingFormValues['periodType']) =>
    setValue('endDate', defaultEndDate(periodType, getValues('startDate'), getValues('endDate')), { shouldDirty: true, shouldValidate: true });

  const sortedBoats = useMemo(() => (boats.data ?? []).filter((b) => b.isActive || b.id === detail?.boatId), [boats.data, detail?.boatId]);
  const statusEditable = !detail || detail.status === 'Active' || detail.status === 'Cancelled';

  return (
    <Dialog
      open
      title={title}
      onClose={onClose}
      locked={saving}
      size="xl"
      headerExtra={detail && <StatusBadges detail={detail} />}
      footer={
        <>
          {formError && <span className={styles.footerError} role="alert">{formError}</span>}
          <Button onClick={onClose} disabled={saving}>Cancel</Button>
          <Button variant="primary" onClick={submit} busy={saving} busyLabel={isNew ? 'Creating…' : 'Saving…'} disabled={isHold}>
            {isNew ? 'Create booking' : 'Save changes'}
          </Button>
        </>
      }
    >
      <FormProvider {...form}>
        {detail && isHold && <HoldBanner detail={detail} />}
        <form onSubmit={submit} noValidate>
          <fieldset className={styles.fieldset} disabled={saving || isHold}>
            <div className={styles.grid}>
              <Field label="Boat" htmlFor={ids.boatId} error={errors.boatId?.message}>
                <select id={ids.boatId} className={inputClass} {...register('boatId')}>
                  <option value="">Choose a boat…</option>
                  {sortedBoats.map((b) => <option key={b.id} value={b.id}>{b.name}{b.isActive ? '' : ' (inactive)'}</option>)}
                </select>
              </Field>
              <Field label="Period type" htmlFor={ids.periodType} error={errors.periodType?.message}>
                <select id={ids.periodType} className={inputClass} {...register('periodType', { onChange: (e) => onPeriodTypeChange(e.target.value) })}>
                  {PERIOD_TYPES.map((p) => <option key={p.value} value={p.value}>{p.label}</option>)}
                </select>
              </Field>
              <Field label="Start date" htmlFor={ids.startDate} error={errors.startDate?.message}>
                <input id={ids.startDate} type="date" className={inputClass}
                  {...register('startDate', {
                    deps: 'endDate', // start < end is checked when either date loses focus, and shown on the end date
                    onChange: (e) => setValue('endDate', defaultEndDate(getValues('periodType'), e.target.value, getValues('endDate')), { shouldDirty: true }),
                  })} />
              </Field>
              <Field label="End date" htmlFor={ids.endDate} error={errors.endDate?.message}>
                <input id={ids.endDate} type="date" className={inputClass} {...register('endDate')} />
              </Field>
              <Field label="Number of adults" htmlFor={ids.numberOfAdults} error={errors.numberOfAdults?.message}>
                <input id={ids.numberOfAdults} type="number" min={1} className={inputClass} {...register('numberOfAdults', { setValueAs: numberOrNull })} />
              </Field>
              <Field label="Number of children" htmlFor={ids.numberOfChildren} error={errors.numberOfChildren?.message}>
                <input id={ids.numberOfChildren} type="number" min={0} className={inputClass} {...register('numberOfChildren', { setValueAs: numberOrNull })} />
              </Field>
              <Field label="Check-in time" htmlFor={ids.checkInTime} error={errors.checkInTime?.message} hint="Standard 11am – 2pm">
                <input id={ids.checkInTime} type="time" className={inputClass} {...register('checkInTime')} />
              </Field>
              <Field label="Check-out time" htmlFor={ids.checkOutTime} error={errors.checkOutTime?.message} hint="Standard from 8am">
                <input id={ids.checkOutTime} type="time" className={inputClass} {...register('checkOutTime')} />
              </Field>
            </div>

            {warnings.length > 0 && (
              <ul className={styles.warnings} aria-label="Warnings">
                {warnings.map((w) => <li key={w}>⚠ {w}</li>)}
              </ul>
            )}

            <h3 className={styles.subheading}>
              Customer
              {frequentFloater?.isFrequentFloater && (
                <Badge tone="warning" title={`${frequentFloater.otherBookingCount} other booking${frequentFloater.otherBookingCount === 1 ? '' : 's'} within a year`}>
                  ⭐ Frequent Floater
                </Badge>
              )}
            </h3>
            <fieldset className={styles.fieldset} aria-label="Customer">
              <div className={styles.grid}>
                <Field label="Email" htmlFor={ids.email} error={errors.email?.message}>
                  <input id={ids.email} type="email" className={inputClass} {...register('email', { onBlur: () => void lookupEmail() })} />
                </Field>
                <Field label="First name" htmlFor={ids.firstName} error={errors.firstName?.message}>
                  <input id={ids.firstName} className={inputClass} {...register('firstName')} />
                </Field>
                <Field label="Last name" htmlFor={ids.lastName} error={errors.lastName?.message}>
                  <input id={ids.lastName} className={inputClass} {...register('lastName')} />
                </Field>
                <Field label="Mobile" htmlFor={ids.mobile} error={errors.mobile?.message}>
                  <Controller control={control} name="mobile" render={({ field }) => (
                    <MobileInput id={ids.mobile} ref={field.ref} value={field.value} onChange={field.onChange} onBlur={field.onBlur} invalid={!!errors.mobile} disabled={isHold || saving} />
                  )} />
                </Field>
                <Field label="Secondary mobile" htmlFor={ids.secondaryMobile} error={errors.secondaryMobile?.message}>
                  <Controller control={control} name="secondaryMobile" render={({ field }) => (
                    <MobileInput id={ids.secondaryMobile} ref={field.ref} value={field.value} onChange={field.onChange} onBlur={field.onBlur} invalid={!!errors.secondaryMobile} disabled={isHold || saving} />
                  )} />
                </Field>
              </div>
              <AddressFields ids={ids} />
            </fieldset>

            <div className={styles.grid}>
              <label className={styles.check}>
                <input type="checkbox" {...register('isStandby')} /> Stand-by booking
              </label>
              <Field label="Status" htmlFor={ids.status}>
                {statusEditable ? (
                  <select id={ids.status} className={inputClass} {...register('status')} disabled={isNew || saving}>
                    <option value="Active">Active</option>
                    <option value="Cancelled">Cancelled</option>
                  </select>
                ) : (
                  <div id={ids.status} className={styles.readonly}>{detail?.status === 'PendingPayment' ? 'Pending payment' : detail?.status}</div>
                )}
              </Field>
            </div>
            <div className={styles.grid}>
              <Field label="Booking comments" htmlFor={ids.bookingComments}>
                <textarea id={ids.bookingComments} className={inputClass} rows={3} {...register('bookingComments')} />
              </Field>
              <Field label="Customer comments" htmlFor={ids.customerComments} hint="Shared by all of this customer's bookings.">
                <textarea id={ids.customerComments} className={inputClass} rows={3} {...register('customerComments')} />
              </Field>
            </div>

            <AddonLinesSection
              addons={addons.data ?? []}
              bookingId={detail?.id ?? null}
              isNew={isNew}
              ids={{ hirePrice: ids.hirePrice }}
              errorHirePrice={errors.hirePrice?.message}
            />
          </fieldset>
        </form>

        <PriceBreakdown amountPaid={detail?.amountPaid ?? null} />

        {detail && <PaymentsSection detail={detail} dirty={isDirty} />}
        {detail && detail.createdBy === 'Customer' && <OnlineInfo detail={detail} />}
      </FormProvider>
    </Dialog>
  );
}

/** The address parts (§12.1). None is required; Australia has a State dropdown and a 4-digit postcode. */
function AddressFields({ ids }: { ids: Record<(typeof FIELDS)[number], string> }) {
  const { register, control, setValue, getValues, formState: { errors } } = useFormContext<BookingFormValues>();
  const country = useWatch({ control, name: 'address.country' });
  const australian = country === AUSTRALIA;
  const e = errors.address;
  const onCountryChange = (next: string) => {
    // Switching to Australia clears a state that isn't one of its states and territories.
    if (next === AUSTRALIA && !AUSTRALIAN_STATES.includes(getValues('address.state').trim().toUpperCase()))
      setValue('address.state', '', { shouldDirty: true });
  };
  return (
    <div className={styles.grid} role="group" aria-label="Address">
      <Field className={styles.wide} label="Address line 1" htmlFor={ids['address.line1']} error={e?.line1?.message}>
        <input id={ids['address.line1']} className={inputClass} autoComplete="off" {...register('address.line1')} />
      </Field>
      <Field className={styles.wide} label="Address line 2" htmlFor={ids['address.line2']} error={e?.line2?.message}>
        <input id={ids['address.line2']} className={inputClass} autoComplete="off" {...register('address.line2')} />
      </Field>
      <Field label="City/Locality" htmlFor={ids['address.locality']} error={e?.locality?.message}>
        <input id={ids['address.locality']} className={inputClass} autoComplete="off" {...register('address.locality')} />
      </Field>
      <Field label="State" htmlFor={ids['address.state']} error={e?.state?.message}>
        {australian ? (
          <select id={ids['address.state']} className={inputClass} {...register('address.state')}>
            <option value="">—</option>
            {AUSTRALIAN_STATES.map((st) => <option key={st} value={st}>{st}</option>)}
          </select>
        ) : (
          <input id={ids['address.state']} className={inputClass} autoComplete="off" {...register('address.state')} />
        )}
      </Field>
      <Field label="Postcode" htmlFor={ids['address.postcode']} error={e?.postcode?.message}>
        <input id={ids['address.postcode']} className={inputClass} autoComplete="off" inputMode={australian ? 'numeric' : undefined}
          {...register('address.postcode')} />
      </Field>
      <Field label="Country" htmlFor={ids['address.country']} error={e?.country?.message}>
        <select id={ids['address.country']} className={inputClass} {...register('address.country', { onChange: (ev) => onCountryChange(ev.target.value) })}>
          {countryOptions().map((c) => <option key={c.code} value={c.code}>{c.name}</option>)}
        </select>
      </Field>
    </div>
  );
}

/** Why a hold is read-only, and what staff can do about it (§12.1 Holds). */
function HoldBanner({ detail }: { detail: BookingDetail }) {
  let text: string;
  if (detail.status === 'PendingPayment')
    text = `Waiting for the customer's online payment. The hold expires at ${formatTimestamp(detail.holdExpiresAt)}.`;
  else if (detail.payments.length > 0) text = 'Paid online after the hold expired — refund in Stripe.';
  else text = `This hold expired without payment. Record a manual payment of at least ${formatMoney(detail.checkoutAmount ?? 0)} to confirm the booking.`;
  return <p className={styles.holdBanner} role="status">{text}</p>;
}

function StatusBadges({ detail }: { detail: BookingDetail }) {
  return (
    <span className={styles.badges}>
      {detail.isStandby && <Badge tone="muted">Stand-by</Badge>}
      <Badge tone={detail.status === 'Active' ? 'success' : 'muted'}>{detail.status === 'PendingPayment' ? 'Pending payment' : detail.status}</Badge>
    </span>
  );
}

function OnlineInfo({ detail }: { detail: BookingDetail }) {
  const d = detail.customerDetails;
  return (
    <section className={styles.online} aria-label="Online booking details">
      <h3 className={styles.subheading}>Booked online</h3>
      <dl className={styles.info}>
        <dt>Created by</dt><dd>Customer (website)</dd>
        <dt>Created</dt><dd>{formatTimestamp(detail.createdDate)}</dd>
        <dt>Terms accepted</dt><dd>{formatTimestamp(detail.termsAcceptedAt) || '—'}</dd>
        <dt>Rooming warning accepted</dt>
        <dd>{detail.roomingWarningAcceptance ? `${formatTimestamp(detail.roomingWarningAcceptance.acceptedAt)} — “${detail.roomingWarningAcceptance.warningTextShown}”` : '—'}</dd>
        {d && (<><dt>Details entered online</dt><dd>{d.firstName} {d.lastName} · {d.email} · {d.mobileNumber}</dd></>)}
      </dl>
    </section>
  );
}
