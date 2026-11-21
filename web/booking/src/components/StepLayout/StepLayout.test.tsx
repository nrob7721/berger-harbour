import { describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { setSmallScreen } from '@/testing/screen-size';
import { StepLayout } from './StepLayout';

function renderLayout(props: { bar?: boolean; error?: string | null } = {}) {
  return render(
    <StepLayout title="A step" back={<button>Back</button>} action={<button>Continue</button>} barContent={<p>Price</p>} {...props}>
      <p>Content</p>
    </StepLayout>,
  );
}

describe('StepLayout and the step action bar', () => {
  it('puts the buttons, the bar content and the error in the bar on a small screen, not in the page', () => {
    setSmallScreen(true);
    renderLayout({ bar: true, error: 'Something went wrong' });
    const bar = within(screen.getByTestId('step-action-bar'));
    expect(bar.getByRole('button', { name: 'Back' })).toBeInTheDocument();
    expect(bar.getByRole('button', { name: 'Continue' })).toBeInTheDocument();
    expect(bar.getByText('Price')).toBeInTheDocument();
    expect(bar.getByRole('alert')).toHaveTextContent('Something went wrong');
    expect(screen.getAllByRole('button')).toHaveLength(2);
    expect(screen.getAllByRole('alert')).toHaveLength(1);
    // The error sits above the buttons.
    expect(bar.getByRole('alert').compareDocumentPosition(bar.getByRole('button', { name: 'Back' })) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('has no bar above 768px', () => {
    renderLayout({ bar: true, error: 'Something went wrong' });
    expect(screen.queryByTestId('step-action-bar')).not.toBeInTheDocument();
    expect(screen.getByText('Price')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Continue' })).toBeInTheDocument();
  });

  it('has no bar for a step that does not opt in', () => {
    setSmallScreen(true);
    renderLayout();
    expect(screen.queryByTestId('step-action-bar')).not.toBeInTheDocument();
  });

  it('follows the screen size as it changes', () => {
    renderLayout({ bar: true });
    expect(screen.queryByTestId('step-action-bar')).not.toBeInTheDocument();
    setSmallScreen(true);
    expect(screen.getByTestId('step-action-bar')).toBeInTheDocument();
    setSmallScreen(false);
    expect(screen.queryByTestId('step-action-bar')).not.toBeInTheDocument();
  });

  it('pads the page by the bar\'s height while it is shown', () => {
    setSmallScreen(true);
    const { unmount } = renderLayout({ bar: true });
    expect(document.documentElement.style.getPropertyValue('--action-bar-height')).toMatch(/px$/);
    unmount();
    expect(document.documentElement.style.getPropertyValue('--action-bar-height')).toBe('');
  });
});
