import { useForm, type FieldValues, type Path, type UseFormProps, type UseFormSetError } from 'react-hook-form';
import { EMAIL_ERROR, isValidEmail } from '../utils/email';
import { isValidMobile, MOBILE_ERROR } from '../utils/phone';
import { ApiError } from './http';

/**
 * Every form in both apps (§6.1): a field is validated when it loses focus, and once it shows an error it is re-validated on
 * every change. Pressing the main button validates every field and focuses the first invalid one; the button stays enabled.
 */
export function useAppForm<TFieldValues extends FieldValues, TContext = unknown, TTransformedValues = TFieldValues>(
  props?: UseFormProps<TFieldValues, TContext, TTransformedValues>,
) {
  return useForm<TFieldValues, TContext, TTransformedValues>({ mode: 'onTouched', reValidateMode: 'onChange', shouldFocusError: true, ...props });
}

/** A `validate` result: `true` when valid, otherwise the message. */
type Check = string | true;

const isBlank = (v: unknown) =>
  v === null || v === undefined || (typeof v === 'string' && v.trim() === '') || (typeof v === 'number' && Number.isNaN(v));

/** Rules for `register(name, { validate })`, matching the server's checks and messages. */
export const rules = {
  required: (message: string) => (v: unknown): Check => (isBlank(v) ? message : true),
  maxLength: (max: number, label: string) => (v: unknown): Check =>
    typeof v === 'string' && v.trim().length > max ? `${label} must be ${max} characters or fewer.` : true,
  /** Empty passes; use with `required` when the email is required. */
  email: (v: unknown): Check => (isBlank(v) || isValidEmail(String(v)) ? true : EMAIL_ERROR),
  /** Empty passes; otherwise a mobile number (§7.1). */
  mobile: (v: unknown): Check => (isBlank(v) || isValidMobile(String(v)) ? true : MOBILE_ERROR),
  /** Empty passes; otherwise a number at least `min` (and at most `max`), optionally whole. */
  number: (opts: { min?: number; minExclusive?: boolean; max?: number; integer?: boolean; message: string }) => (v: unknown): Check => {
    if (isBlank(v)) return true;
    const n = Number(v);
    if (Number.isNaN(n)) return opts.message;
    if (opts.integer && !Number.isInteger(n)) return opts.message;
    if (opts.min !== undefined && (opts.minExclusive ? n <= opts.min : n < opts.min)) return opts.message;
    if (opts.max !== undefined && n > opts.max) return opts.message;
    return true;
  },
};

/**
 * Maps a 400 ProblemDetails' per-field errors onto the form's fields. Server keys are camelCase API names; `aliases`
 * maps them to form paths when they differ. Returns the errors that matched no field.
 */
export function applyServerErrors<T extends FieldValues>(
  error: unknown,
  setError: UseFormSetError<T>,
  fields: readonly string[],
  aliases: Record<string, string> = {},
): string[] {
  if (!(error instanceof ApiError)) return [];
  const unmatched: string[] = [];
  for (const [key, messages] of Object.entries(error.errors)) {
    const target = aliases[key] ?? fields.find((f) => f.toLowerCase() === key.toLowerCase());
    if (target) setError(target as Path<T>, { type: 'server', message: messages[0] });
    else unmatched.push(...messages);
  }
  return unmatched;
}
