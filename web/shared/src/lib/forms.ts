import type { FieldValues, Path, UseFormSetError } from 'react-hook-form';
import { ApiError } from './http';

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
