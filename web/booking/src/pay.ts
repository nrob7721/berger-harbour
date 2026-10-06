import { formatLongDate } from '@shared/dates';
import { $, clear, h } from '@shared/dom';
import { formatMoney } from '@shared/format';
import { ApiError } from '@shared/http';
import { publicApi, type PayPage } from './api';
import { mountCheckout } from './stripe';

// The pay page reached from emailed payment links: /pay/?token=…

const container = $('#pay');
const token = new URLSearchParams(location.search).get('token') ?? '';

function message(text: string, kind: 'error' | 'success' | 'info' = 'info'): void {
  clear(container);
  container.append(h('div', { class: `notice notice--${kind}` }, text));
}

function render(page: PayPage): void {
  clear(container);
  const row = (label: string, value: string) => h('div', { class: 'summary__row' }, h('dt', {}, label), h('dd', {}, value));
  container.append(
    h('dl', { class: 'summary' },
      row('Booking reference', page.reference),
      row('Boat', page.boatName),
      row('Dates', `${formatLongDate(page.startDate)} – ${formatLongDate(page.endDate)}`),
      row('Total', formatMoney(page.totalPrice)),
      row('Paid so far', formatMoney(page.amountPaid)),
      row('Still owing', formatMoney(page.amountOwing)),
    ),
  );
  if (!page.canPay) {
    container.append(h('div', { class: 'notice notice--success' }, 'Nothing to pay — thank you, there is no amount due on this booking.'));
    return;
  }
  const button = h('button', { type: 'button', class: 'button button--primary' }, `Pay ${formatMoney(page.amountDueNow)}`);
  button.addEventListener('click', async () => {
    button.disabled = true;
    try {
      const checkout = await publicApi.payCheckout(token);
      button.remove();
      await mountCheckout($('#checkout'), checkout.clientSecret);
      message(`Thank you — your payment for ${page.reference} was successful. A receipt will be emailed to you.`, 'success');
    } catch (e) {
      button.disabled = false;
      container.append(h('div', { class: 'notice notice--error' },
        e instanceof ApiError ? e.messages.join(' ') : (e as Error).message));
    }
  });
  container.append(h('p', { class: 'summary__due' }, 'Amount due now: ', h('strong', {}, formatMoney(page.amountDueNow))), button);
}

async function init(): Promise<void> {
  if (!token) {
    message('This payment link is not valid. Please use the link from your email.', 'error');
    return;
  }
  try {
    render(await publicApi.payPage(token));
  } catch (e) {
    message(e instanceof ApiError && e.status === 404
      ? 'This payment link is not valid. Please use the link from your email or contact us.'
      : 'Sorry, payments are not available right now. Please try again shortly.', 'error');
  }
}

void init();
