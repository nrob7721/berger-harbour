import type { BoatDetails } from '@/lib/types';

/** The same text the server generates; it is sent with the booking and must match exactly. */
export function roomingWarningText(boat: Pick<BoatDetails, 'adultBeds' | 'kidsBeds' | 'beddingDescription'>, guests: number): string {
  return (
    `This boat has ${boat.adultBeds} adult beds and ${boat.kidsBeds} children's beds (${boat.beddingDescription ?? ''}). ` +
    `With ${guests} guests, some guests will need to share beds. Please make sure your group's sleeping arrangements suit this layout.`
  );
}
