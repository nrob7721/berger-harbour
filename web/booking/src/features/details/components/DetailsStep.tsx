import { useId, useRef, useState, type BaseSyntheticEvent } from 'react';
import { Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { z } from 'zod';
import { Turnstile } from '@marsidev/react-turnstile';
import { Button } from '@shared/components/Button/Button';
import { Field, inputClass } from '@shared/components/Field/Field';
import { MobileInput } from '@shared/components/MobileInput/MobileInput';
import { applyServerErrors, useAppForm } from '@shared/lib/forms';
import { ApiError, errorMessage } from '@shared/lib/http';
import { EMAIL_ERROR, isValidEmail } from '@shared/utils/email';
import { isValidMobile, MOBILE_ERROR } from '@shared/utils/phone';
import { StepLayout } from '@/components/StepLayout/StepLayout';
import { env } from '@/config/env';
import { api } from '@/lib/api';
import { useBooking, type ContactDetails } from '@/lib/booking-state';
import { draftBody } from '@/lib/draft';
import styles from './DetailsStep.module.css';

const schema = z.object({
  firstName: z.string().trim().min(1, 'First name is required.').max(100),
  lastName: z.string().trim().min(1, 'Last name is required.').max(100),
  email: z.string().trim().min(1, 'Email is required.').refine(isValidEmail, EMAIL_ERROR),
  mobile: z.string().min(1, 'Mobile is required.').refine((v) => isValidMobile(v), MOBILE_ERROR),
});

type Form = z.infer<typeof schema>;
const FIELDS = ['firstName', 'lastName', 'email', 'mobile'] as const;

export function DetailsStep() {
  const { state, dispatch } = useBooking();
  const ids = { first: useId(), last: useId(), email: useId(), mobile: useId() };
  const [token, setToken] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const needsTurnstile = !state.draftId;
  // handleSubmit validates asynchronously, so the button is not busy for a moment after a click. This closes that gap.
  const submitting = useRef(false);
  // Each field is validated when it loses focus (§6.1).
  const { register, handleSubmit, control, setError, formState: { errors } } = useAppForm<Form>({
    resolver: zodResolver(schema),
    defaultValues: state.details ?? { firstName: '', lastName: '', email: '', mobile: '' },
  });

  const save = useMutation({
    mutationFn: async (details: ContactDetails) => {
      const withDetails = { ...state, details };
      if (state.draftId) {
        await api.updateDraft(state.draftId, draftBody(withDetails, 'Addons')!);
        return state.draftId;
      }
      const { draftId } = await api.createDraft({
        slug: state.selectedSlug!, periodType: state.periodType, startDate: state.period!.start, numberOfAdults: state.adults,
        numberOfChildren: state.children, ...details, turnstileToken: token,
      });
      return draftId;
    },
    onSuccess: (draftId, details) => {
      dispatch({ type: 'details', details });
      dispatch({ type: 'draft', draftId });
      dispatch({ type: 'goto', step: 'addons' });
    },
    onSettled: () => {
      submitting.current = false;
    },
    onError: (e) => {
      if (e instanceof ApiError && e.status === 404 && state.draftId) {
        dispatch({ type: 'draftLost' });
        setFormError('Your session expired. Please complete the security check and continue again.');
        return;
      }
      const unmatched = applyServerErrors(e, setError, FIELDS);
      const allMapped = e instanceof ApiError && Object.keys(e.errors).length > 0 && unmatched.length === 0;
      setFormError(allMapped ? null : (unmatched[0] ?? errorMessage(e)));
    },
  });

  const submit = async (event?: BaseSyntheticEvent) => {
    if (submitting.current) {
      event?.preventDefault();
      return;
    }
    submitting.current = true;
    let started = false;
    try {
      await handleSubmit((values) => {
        started = true;
        setFormError(null);
        save.mutate({ firstName: values.firstName.trim(), lastName: values.lastName.trim(), email: values.email.trim(), mobile: values.mobile });
      })(event);
    } finally {
      if (!started) submitting.current = false; // invalid: nothing was sent, so allow another attempt
    }
  };

  return (
    <StepLayout
      title="Your details"
      error={formError}
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
