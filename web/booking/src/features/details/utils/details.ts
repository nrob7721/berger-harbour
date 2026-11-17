import { z } from 'zod';
import { ApiError, errorMessage } from '@shared/lib/http';
import { EMAIL_ERROR, isValidEmail } from '@shared/utils/email';
import { isValidMobile, MOBILE_ERROR } from '@shared/utils/phone';
import { api } from '@/lib/api';
import type { BookingState } from '@/lib/booking-state';
import { draftBody } from '@/lib/draft';
import type { StepOutcome } from '@/lib/progress';

export const detailsSchema = z.object({
  firstName: z.string().trim().min(1, 'First name is required.').max(100),
  lastName: z.string().trim().min(1, 'Last name is required.').max(100),
  email: z.string().trim().min(1, 'Email is required.').refine(isValidEmail, EMAIL_ERROR),
  mobile: z.string().min(1, 'Mobile is required.').refine((v) => isValidMobile(v), MOBILE_ERROR),
});

export const SESSION_EXPIRED = 'Your session expired. Please complete the security check and continue again.';

/**
 * The Your details Continue for a step a forward jump passes: the details already entered must be valid, and the draft is
 * updated as its Continue does. A booking without a draft stops here, because creating one needs the security check.
 */
export async function passDetails(state: BookingState): Promise<StepOutcome> {
  if (!state.details || !detailsSchema.safeParse(state.details).success) return { ok: false, stop: 'details', showErrors: true };
  if (!state.draftId) return { ok: false, stop: 'details' };
  try {
    await api.updateDraft(state.draftId, draftBody(state, 'Addons')!);
    return { ok: true };
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) return { ok: false, stop: 'details', notice: SESSION_EXPIRED, patch: { draftId: null } };
    return { ok: false, stop: 'details', notice: errorMessage(e) };
  }
}
