import { useEffect, useId, useState } from 'react';
import { Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import type { z } from 'zod';
import { Turnstile } from '@marsidev/react-turnstile';
import { Button } from '@shared/components/Button/Button';
import { Field, inputClass } from '@shared/components/Field/Field';
import { MobileInput } from '@shared/components/MobileInput/MobileInput';
import { applyServerErrors, useAppForm } from '@shared/lib/forms';
import { ApiError, errorMessage } from '@shared/lib/http';
import { StepLayout } from '@/components/StepLayout/StepLayout';
import { env } from '@/config/env';
import { api } from '@/lib/api';
import { isAfter, periodTypeOfState, useBooking, type BookingState, type ContactDetails } from '@/lib/booking-state';
import { draftBody } from '@/lib/draft';
import { detailsSchema as schema, SESSION_EXPIRED } from '../utils/details';
import styles from './DetailsStep.module.css';

type Form = z.infer<typeof schema>;
const FIELDS = ['firstName', 'lastName', 'email', 'mobile'] as const;

/** Your details. `onContinued` gets what Continue changed, so a progress-step jump can carry on from it. */
export function DetailsStep({ onContinued }: { onContinued: (changes: Partial<BookingState>) => void }) {
  const { state, dispatch } = useBooking();
  const ids = { first: useId(), last: useId(), email: useId(), mobile: useId() };
  const [token, setToken] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const needsTurnstile = !state.draftId;
  // Each field is validated when it loses focus (§6.1).
  const { register, handleSubmit, control, setError, trigger, watch, getValues, formState: { errors } } = useAppForm<Form>({
    resolver: zodResolver(schema),
    defaultValues: state.details ?? { firstName: '', lastName: '', email: '', mobile: '' },
  });

  // Edits are kept in the step state exactly as entered, unvalidated and unsaved, so leaving the step backwards (Back or an
  // earlier progress step) keeps them. The next forward move validates and saves them (decision 41).
  useEffect(() => {
    const { unsubscribe } = watch(() => dispatch({ type: 'details', details: getValues() }));
    return unsubscribe;
  }, [watch, getValues, dispatch]);

  const save = useMutation({
    mutationFn: async (details: ContactDetails) => {
      const withDetails = { ...state, details };
      if (state.draftId) {
        await api.updateDraft(state.draftId, draftBody(withDetails, 'Addons')!);
        return state.draftId;
      }
      const { draftId } = await api.createDraft({
        slug: state.selectedSlug!, periodType: periodTypeOfState(state)!, startDate: state.period!.start, numberOfAdults: state.adults,
        numberOfChildren: state.children, ...details, turnstileToken: token,
      });
      return draftId;
    },
    onSuccess: (draftId, details) => {
      dispatch({ type: 'details', details });
      dispatch({ type: 'draft', draftId });
      onContinued({ details, draftId });
    },
    onError: (e) => {
      dispatch({ type: 'jumpCancelled' });
      if (e instanceof ApiError && e.status === 404 && state.draftId) {
        dispatch({ type: 'draftLost' });
        setFormError(SESSION_EXPIRED);
        return;
      }
      const unmatched = applyServerErrors(e, setError, FIELDS);
      const allMapped = e instanceof ApiError && Object.keys(e.errors).length > 0 && unmatched.length === 0;
      setFormError(allMapped ? null : (unmatched[0] ?? errorMessage(e)));
    },
  });

  const submit = handleSubmit((values) => {
    setFormError(null);
    save.mutate({ firstName: values.firstName.trim(), lastName: values.lastName.trim(), email: values.email.trim(), mobile: values.mobile });
  }, () => dispatch({ type: 'jumpCancelled' }));

  // A forward jump stopped here on invalid details: show their errors.
  useEffect(() => {
    if (state.showErrors) void trigger();
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // A later progress step was clicked: run this step's Continue first (decision 41).
  useEffect(() => {
    if (!state.jumpTarget || !isAfter(state.jumpTarget, 'details') || save.isPending) return;
    if (needsTurnstile && !token) {
      dispatch({ type: 'jumpCancelled' });
      setFormError('Please complete the security check, then continue.');
      return;
    }
    void submit();
  }, [state.jumpTarget]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <StepLayout
      title="Your details"
      bar
      error={formError ?? state.notice}
      back={<Button onClick={() => dispatch({ type: 'goto', step: 'dates' })} disabled={save.isPending}>Back</Button>}
      action={
        <Button variant="primary" onClick={submit} busy={save.isPending} busyLabel="Saving…" disabled={needsTurnstile && !token}>
          Continue
        </Button>
      }
    >
      <form className={styles.form} onSubmit={submit} noValidate>
        <fieldset className={styles.fields} disabled={save.isPending}>
          <Field label="First name" htmlFor={ids.first} error={errors.firstName?.message}>
            <input id={ids.first} className={inputClass} autoComplete="given-name" {...register('firstName')} aria-invalid={!!errors.firstName} />
          </Field>
          <Field label="Last name" htmlFor={ids.last} error={errors.lastName?.message}>
            <input id={ids.last} className={inputClass} autoComplete="family-name" {...register('lastName')} aria-invalid={!!errors.lastName} />
          </Field>
          <Field label="Email" htmlFor={ids.email} error={errors.email?.message}>
            <input id={ids.email} type="email" className={inputClass} autoComplete="email" {...register('email')} aria-invalid={!!errors.email} />
          </Field>
          <Field label="Mobile" htmlFor={ids.mobile} error={errors.mobile?.message}>
            <Controller
              control={control}
              name="mobile"
              render={({ field }) => (
                <MobileInput id={ids.mobile} ref={field.ref} value={field.value} onChange={field.onChange} onBlur={field.onBlur} invalid={!!errors.mobile} disabled={save.isPending} />
              )}
            />
          </Field>
        </fieldset>
        <button type="submit" hidden aria-hidden="true" tabIndex={-1} />
      </form>
      {needsTurnstile && (
        <div className={styles.turnstile}>
          <Turnstile siteKey={env.turnstileSiteKey} onSuccess={setToken} onExpire={() => setToken(null)} onError={() => setToken(null)} />
        </div>
      )}
      <p className={styles.note}>If you don't finish your booking, our team may contact you to help.</p>
    </StepLayout>
  );
}
