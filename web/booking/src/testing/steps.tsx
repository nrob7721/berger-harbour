import { useAdvance } from '@/app/useAdvance';
import { AddonsStep } from '@/features/addons/components/AddonsStep';
import { DetailsStep } from '@/features/details/components/DetailsStep';

/** Your details wired as the booking app wires it: Continue opens Add-ons, or carries a progress-step jump on. */
export function DetailsStepInApp() {
  const advance = useAdvance();
  return <DetailsStep onContinued={(changes) => void advance('addons', changes)} />;
}

/** Add-ons wired as the booking app wires it. */
export function AddonsStepInApp() {
  const advance = useAdvance();
  return <AddonsStep onContinued={() => void advance('summary')} />;
}
