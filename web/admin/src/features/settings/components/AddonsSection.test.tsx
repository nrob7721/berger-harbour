import { describe, expect, it } from 'vitest';
import { fireEvent, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { addons } from '@/testing/server';
import { renderAdmin } from '@/testing/render';
import { AddonDialog } from './AddonsSection';

describe('Add-on settings', () => {
  it('price on request disables and clears the unit prices', async () => {
    renderAdmin(<AddonDialog addon={addons[0]} onClose={() => {}} />);
    expect(screen.getByLabelText('Mid-week price')).toHaveValue(5);
    await userEvent.setup().click(screen.getByLabelText('Price on request'));
    for (const label of ['Mid-week price', 'Weekend price', 'Week price']) {
      expect(screen.getByLabelText(label)).toBeDisabled();
      expect(screen.getByLabelText(label)).toHaveValue(null);
    }
  });

  it('an empty capacity disables and clears the unit label', async () => {
    renderAdmin(<AddonDialog addon={addons[2]} onClose={() => {}} />);
    expect(screen.getByLabelText('Capacity unit label')).toHaveValue('car spots');
    expect(screen.getByLabelText('Capacity unit label')).toBeEnabled();
    fireEvent.change(screen.getByLabelText('Capacity'), { target: { value: '' } });
    expect(screen.getByLabelText('Capacity unit label')).toBeDisabled();
    expect(screen.getByLabelText('Capacity unit label')).toHaveValue('');
  });

  it('a capacity without a unit label shows "Required" and saves nothing', async () => {
    renderAdmin(<AddonDialog addon={addons[0]} onClose={() => {}} />);
    const user = userEvent.setup();
    fireEvent.change(screen.getByLabelText('Capacity'), { target: { value: '4' } });
    expect(screen.getByLabelText('Capacity unit label')).toBeEnabled();
    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Required')).toBeInTheDocument();
  });
});
