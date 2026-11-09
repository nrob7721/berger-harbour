import { useEffect, useId } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Button } from '@shared/components/Button/Button';
import { Field, inputClass } from '@shared/components/Field/Field';
import { useToast } from '@shared/components/Toast/Toast';
import { applyServerErrors, rules, useAppForm } from '@shared/lib/forms';
import { errorMessage } from '@shared/lib/http';
import { Section } from '@/components/Section';
import { useBusinessSettings } from '@/hooks/useReferenceData';
import { api, keys } from '@/lib/api';
import type { BusinessSettings } from '@/lib/types';
import styles from './Settings.module.css';

type Form = Omit<BusinessSettings, 'version'>;
const digits = (v: string | null | undefined, strip: RegExp) => (v ?? '').replace(strip, '');
const isWebAddress = (v: string | null | undefined) => {
  if (!v?.trim()) return true; // the server uses the default terms page
  try {
    return ['https:', 'http:'].includes(new URL(v.trim()).protocol);
  } catch {
    return false;
  }
};

const FIELDS = ['depositAmount', 'minimumLeadTimeDays', 'bookingsOpenUntil', 'staffAlertEmail', 'contactPhone', 'contactEmail', 'hireTermsUrl', 'accountName', 'bsb', 'accountNumber'] as const;

export function GeneralSection() {
  const settings = useBusinessSettings();
  const queryClient = useQueryClient();
  const toast = useToast();
  const base = useId();
  const id = (f: string) => `${base}-${f}`;
  // Each field is validated when it loses focus (§6.1).
  const { register, handleSubmit, reset, setError, formState: { errors, isDirty } } = useAppForm<Form>();
  useEffect(() => {
    if (settings.data) reset({ ...settings.data, bsb: settings.data.bsb ? `${settings.data.bsb.slice(0, 3)}-${settings.data.bsb.slice(3)}` : '' });
  }, [settings.data, reset]);

  const save = useMutation({
    mutationFn: (v: Form) => api.updateBusiness({
      ...v, depositAmount: Number(v.depositAmount), minimumLeadTimeDays: Number(v.minimumLeadTimeDays), version: settings.data?.version ?? 0,
      staffAlertEmail: v.staffAlertEmail || null, contactPhone: v.contactPhone || null, contactEmail: v.contactEmail || null,
      accountName: v.accountName || null, bsb: v.bsb || null, accountNumber: v.accountNumber || null,
    }),
    onSuccess: (saved) => {
      queryClient.setQueryData(keys.business, saved);
      toast.show('Settings saved.');
    },
    onError: (e) => applyServerErrors(e, setError, FIELDS),
  });
  const submit = handleSubmit((v) => save.mutate(v));

  return (
    <Section id="general" title="General">
      <form onSubmit={submit} noValidate>
        <fieldset className={styles.fieldset} disabled={save.isPending || !settings.data}>
          <div className={styles.formGrid}>
            <Field label="Default deposit" htmlFor={id('deposit')} error={errors.depositAmount?.message} hint="Used for boats without their own deposit">
              <input id={id('deposit')} type="number" step="0.01" min={0} className={inputClass} {...register('depositAmount', {
                validate: {
                  required: rules.required('The default deposit must be more than $0.'),
                  min: rules.number({ min: 0, minExclusive: true, message: 'The default deposit must be more than $0.' }),
                },
              })} />
            </Field>
            <Field label="Minimum lead time (days)" htmlFor={id('lead')} error={errors.minimumLeadTimeDays?.message}>
              <input id={id('lead')} type="number" min={0} className={inputClass} {...register('minimumLeadTimeDays', {
                validate: {
                  required: rules.required('Lead time must be between 0 and 365 days.'),
                  range: rules.number({ min: 0, max: 365, integer: true, message: 'Lead time must be between 0 and 365 days.' }),
                },
              })} />
            </Field>
            <Field label="Bookings open until" htmlFor={id('open')} error={errors.bookingsOpenUntil?.message} hint="Move forward each year after entering blocked periods and prices">
              <input id={id('open')} type="date" className={inputClass} {...register('bookingsOpenUntil')} />
            </Field>
            <Field label="Staff alert email" htmlFor={id('staff')} error={errors.staffAlertEmail?.message}>
              <input id={id('staff')} type="email" className={inputClass} {...register('staffAlertEmail', { validate: rules.email })} />
            </Field>
            <Field label="Contact phone" htmlFor={id('phone')} error={errors.contactPhone?.message}>
              <input id={id('phone')} className={inputClass} {...register('contactPhone', { validate: rules.maxLength(40, 'Contact phone') })} />
            </Field>
            <Field label="Contact email" htmlFor={id('email')} error={errors.contactEmail?.message}>
              <input id={id('email')} type="email" className={inputClass} {...register('contactEmail', { validate: rules.email })} />
            </Field>
            <Field className={styles.wide} label="Hire terms URL" htmlFor={id('terms')} error={errors.hireTermsUrl?.message}>
              <input id={id('terms')} type="url" className={inputClass} {...register('hireTermsUrl', { validate: (v) => isWebAddress(v) || 'Enter a full web address starting with https://.' })} />
            </Field>
          </div>
          <h3 className={styles.subheading}>Bank account</h3>
          <p className={styles.help}>
            Optional. Available to email templates as {'{{AccountName}}'}, {'{{BSB}}'} and {'{{AccountNumber}}'}, e.g. to ask for a bank transfer instead of a payment link.
          </p>
          <div className={styles.formGrid}>
            <Field label="Account name" htmlFor={id('acct')} error={errors.accountName?.message}>
              <input id={id('acct')} className={inputClass} {...register('accountName', { validate: rules.maxLength(100, 'Account name') })} />
            </Field>
            <Field label="BSB" htmlFor={id('bsb')} error={errors.bsb?.message}>
              <input id={id('bsb')} className={inputClass} placeholder="123-456"
                {...register('bsb', { validate: (v) => !v?.trim() || /^\d{6}$/.test(digits(v, /[-\s]/g)) || 'BSB must be 6 digits, e.g. 123-456.' })} />
            </Field>
            <Field label="Account number" htmlFor={id('num')} error={errors.accountNumber?.message}>
              <input id={id('num')} className={inputClass} {...register('accountNumber', { validate: (v) => !v?.trim() || /^\d{6,10}$/.test(digits(v, /\s/g)) || 'Account number must be 6 to 10 digits.' })} />
            </Field>
          </div>
          <div className={styles.formActions}>
            {save.isError && Object.keys(errors).length === 0 && <span role="alert" className={styles.error}>{errorMessage(save.error)}</span>}
            <Button variant="primary" type="submit" busy={save.isPending} busyLabel="Saving…" disabled={!isDirty}>Save</Button>
          </div>
        </fieldset>
      </form>
    </Section>
  );
}
