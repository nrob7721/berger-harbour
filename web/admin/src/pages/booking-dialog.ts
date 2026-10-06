import { addDays, formatDate, sydneyToday } from '@shared/dates';
import { clear, h, toast } from '@shared/dom';
import { formatMoney } from '@shared/format';
import { adminApi, type Addon, type AddonLineStatus, type BoatSummary, type BookingDetail, type BookingStatus, type PeriodType } from '../api';
import { clearErrors, field, numberOrNull, openModal, PAYMENT_STATUS_LABEL, PERIOD_LABEL, showError, STATUS_LABEL, value } from '../ui';

interface Options {
  id?: string;
  boatId?: string;
  startDate?: string;
  onSaved: () => Promise<void> | void;
}

const NIGHTS: Partial<Record<PeriodType, number>> = { Midweek: 4, Weekend: 3, Week: 7, LongWeekend: 4 };

const formatInstant = (iso: string | null | undefined) =>
  iso ? new Date(iso).toLocaleString('en-AU', { timeZone: 'Australia/Sydney', dateStyle: 'short', timeStyle: 'short' }) : '—';

export async function openBookingDialog(options: Options): Promise<void> {
  const modal = openModal(options.id ? 'Booking' : 'New booking', { wide: true });
  modal.body.append(h('p', { class: 'muted' }, 'Loading…'));
  let boats: BoatSummary[];
  let addons: Addon[];
  let detail: BookingDetail | null = null;
  try {
    [boats, addons, detail] = await Promise.all([
      adminApi.boats(),
      adminApi.addons(),
      options.id ? adminApi.booking(options.id) : Promise.resolve(null),
    ]);
  } catch (e) {
    clear(modal.body);
    showError(modal.body, e);
    return;
  }

  let priceTouched = !!detail;
  let saving = false;

  const render = () => {
    clear(modal.body);
    clear(modal.footer);
    modal.setTitle(detail ? `Booking ${detail.reference}` : 'New booking');
    const form = h('form', { class: 'booking-form', novalidate: true });
    const left = h('div', { class: 'booking-form__main' });
    const right = h('div', { class: 'booking-form__side' });
    form.append(left, right);
    modal.body.append(form);

    const editable = !detail || detail.status === 'Active' || detail.status === 'Cancelled';
    const boatOptions = boats
      .filter((b) => b.type === 'House' && (b.isActive || b.id === detail?.boatId))
      .map((b) => ({ value: b.id, label: b.isActive ? b.name : `${b.name} (inactive)` }));

    const start = detail?.startDate ?? options.startDate ?? addDays(sydneyToday(), 30);
    const period: PeriodType = detail?.periodType ?? 'Custom';
    if (detail) {
      left.append(h('p', { class: 'booking-meta' },
        h('span', { class: `status-pill status-pill--${detail.status}` }, STATUS_LABEL[detail.status] ?? detail.status),
        ` ${detail.isStandby ? 'Stand-by · ' : ''}Created ${formatInstant(detail.createdDate)} by ${detail.createdBy === 'Customer' ? 'customer (website)' : 'staff'}`));
    }
    left.append(
      h('div', { class: 'form-grid' },
        field({ label: 'Boat', name: 'boatId', required: true, value: detail?.boatId ?? options.boatId ?? boatOptions[0]?.value, options: boatOptions }),
        field({ label: 'Period type', name: 'periodType', required: true, value: period,
          options: Object.entries(PERIOD_LABEL).map(([v, l]) => ({ value: v, label: l })) }),
        field({ label: 'Start date', name: 'startDate', type: 'date', required: true, value: start }),
        field({ label: 'End date', name: 'endDate', type: 'date', required: true, value: detail?.endDate ?? addDays(start, NIGHTS[period] ?? 1) }),
        field({ label: 'Number of guests', name: 'numberOfGuests', type: 'number', required: true, value: detail?.numberOfGuests ?? 2, attrs: { min: 1, max: 99 } }),
        h('div', { class: 'field field--warning', id: 'guest-warning', 'aria-live': 'polite' }),
      ),
      h('h3', {}, 'Customer'),
      h('div', { class: 'form-grid' },
        field({ label: 'Email', name: 'email', type: 'email', required: true, value: detail?.customer.email, hint: 'Existing customers are filled in automatically.' }),
        field({ label: 'Full name', name: 'fullName', required: true, value: detail?.customer.fullName }),
        field({ label: 'Phone', name: 'phone', type: 'tel', value: detail?.customer.mobileNumber }),
      ),
      h('div', { class: 'form-grid' },
        h('div', { class: 'field', 'data-field': 'isStandby' },
          h('label', { class: 'toggle' }, h('input', { type: 'checkbox', name: 'isStandby', checked: detail?.isStandby ?? false }), ' Stand-by booking'),
          h('span', { class: 'field__hint' }, 'Stand-bys never block dates, get no emails and are superseded by a real booking.')),
        detail && !editable
          ? h('div', { class: 'field' }, h('span', { class: 'field__label' }, 'Status'), h('span', {}, STATUS_LABEL[detail.status] ?? detail.status))
          : field({ label: 'Status', name: 'status', value: detail?.status ?? 'Active', options: [
            { value: 'Active', label: 'Active' }, { value: 'Cancelled', label: 'Cancelled' }] }),
      ),
      field({ label: 'Comments', name: 'comments', textarea: true, value: detail?.comments }),
      h('div', { class: 'price-row' },
        field({ label: 'Hire price (AUD, incl. GST)', name: 'hirePrice', type: 'number', required: true, value: detail?.hirePrice ?? '', attrs: { min: 0, step: '0.01' } }),
        detail ? h('button', { type: 'button', class: 'button', id: 'recalculate', disabled: !editable }, 'Recalculate from rates') : null,
        h('span', { class: 'field__hint', id: 'price-hint' })),
    );

    if (!editable && detail) {
      left.querySelectorAll<HTMLInputElement>('input, select').forEach((el) => {
        if (el.name !== 'email' && el.name !== 'fullName' && el.name !== 'phone') el.disabled = true;
      });
      left.prepend(h('div', { class: 'notice notice--warning' }, `This booking is ${STATUS_LABEL[detail.status]}; only comments and customer details can be changed.`));
    }

    if (detail) {
      right.append(addonsSection(detail), paymentsSection(detail));
      if (detail.createdBy === 'Customer') right.append(onlineSection(detail));
    } else {
      right.append(h('p', { class: 'muted' }, 'Add-ons and payments can be added after the booking is saved.'));
    }

    wireForm(form);
    modal.footer.append(
      h('button', { type: 'button', class: 'button', onclick: () => modal.close() }, 'Close'),
      h('button', { type: 'button', class: 'button button--primary', onclick: () => void save(form) }, detail ? 'Save changes' : 'Create booking'),
    );
    updateGuestWarning(form);
  };

  // ---- Form behaviour ----------------------------------------------------------------------------------------------

  const wireForm = (form: HTMLFormElement) => {
    const periodSel = form.querySelector<HTMLSelectElement>('[name="periodType"]')!;
    const startIn = form.querySelector<HTMLInputElement>('[name="startDate"]')!;
    const boatSel = form.querySelector<HTMLSelectElement>('[name="boatId"]')!;
    const endIn = form.querySelector<HTMLInputElement>('[name="endDate"]')!;
    const priceIn = form.querySelector<HTMLInputElement>('[name="hirePrice"]')!;
    const emailIn = form.querySelector<HTMLInputElement>('[name="email"]')!;
    const guestsIn = form.querySelector<HTMLInputElement>('[name="numberOfGuests"]')!;

    priceIn.addEventListener('input', () => (priceTouched = true));
    guestsIn.addEventListener('input', () => updateGuestWarning(form));
    boatSel.addEventListener('change', () => updateGuestWarning(form));

    // Selecting a period type pre-fills the end date; for new bookings the price is pre-filled from the rates.
    const prefill = async () => {
      const periodType = periodSel.value as PeriodType;
      const nights = NIGHTS[periodType];
      if (nights && startIn.value) endIn.value = addDays(startIn.value, nights);
      if (!startIn.value || !boatSel.value) return;
      try {
        const quote = await adminApi.quote(boatSel.value, periodType, startIn.value);
        const hint = form.querySelector('#price-hint')!;
        hint.textContent = quote.hirePrice !== null
          ? `Rate table: ${formatMoney(quote.hirePrice)} (${quote.seasonName})${quote.paymentSchedule === 'Extended' ? ' · extended payment schedule' : ''}`
          : quote.message ?? '';
        if (!detail && !priceTouched && quote.hirePrice !== null) priceIn.value = String(quote.hirePrice);
      } catch {
        // the hint is optional
      }
    };
    periodSel.addEventListener('change', () => void prefill());
    startIn.addEventListener('change', () => void prefill());
    boatSel.addEventListener('change', () => void prefill());
    if (!detail) void prefill();

    emailIn.addEventListener('blur', async () => {
      if (!emailIn.value.includes('@')) return;
      try {
        const customer = await adminApi.customerByEmail(emailIn.value.trim());
        const name = form.querySelector<HTMLInputElement>('[name="fullName"]')!;
        const phone = form.querySelector<HTMLInputElement>('[name="phone"]')!;
        if (!name.value) name.value = customer.fullName;
        if (!phone.value && customer.mobileNumber) phone.value = customer.mobileNumber;
        toast(`Existing customer: ${customer.fullName}`);
      } catch {
        // new customer
      }
    });

    form.querySelector('#recalculate')?.addEventListener('click', () => void run(form, () => adminApi.recalculatePrice(detail!.id, detail!.version)));
  };

  const updateGuestWarning = (form: HTMLElement) => {
    const warning = form.querySelector('#guest-warning');
    if (!warning) return;
    const boat = boats.find((b) => b.id === value(form, 'boatId'));
    const guests = Number(value(form, 'numberOfGuests'));
    const messages: string[] = [];
    if (boat && guests > boat.maxNoOfGuests) messages.push(`More than the boat's maximum of ${boat.maxNoOfGuests} guests.`);
    else if (boat?.noOfBeds && guests > boat.noOfBeds) messages.push(`More guests than beds (${boat.noOfBeds}); some will share.`);
    warning.textContent = messages.join(' ');
  };

  /** Runs a sub-operation (add-on, payment, recalculate) and refreshes the dialog with the new version. */
  const run = async (root: HTMLElement, action: () => Promise<BookingDetail>, success?: string) => {
    try {
      detail = await action();
      priceTouched = true;
      render();
      if (success) toast(success, 'success');
      await options.onSaved();
    } catch (e) {
      showError(root, e, reload);
    }
  };

  const reload = async () => {
    if (!detail) return;
    detail = await adminApi.booking(detail.id);
    render();
  };

  const save = async (form: HTMLFormElement) => {
    if (saving) return;
    clearErrors(modal.body);
    const hirePrice = numberOrNull(value(form, 'hirePrice'));
    const common = {
      boatId: value(form, 'boatId'),
      periodType: value(form, 'periodType') as PeriodType,
      startDate: value(form, 'startDate'),
      endDate: value(form, 'endDate') || null,
      numberOfGuests: Number(value(form, 'numberOfGuests')) || 0,
      email: value(form, 'email') || null,
      fullName: value(form, 'fullName') || null,
      phone: value(form, 'phone') || null,
      isStandby: form.querySelector<HTMLInputElement>('[name="isStandby"]')!.checked,
      comments: value(form, 'comments') || null,
    };
    saving = true;
    try {
      if (detail) {
        detail = await adminApi.updateBooking(detail.id, {
          ...common,
          version: detail.version,
          hirePrice: hirePrice ?? 0,
          status: (value(form, 'status') || detail.status) as BookingStatus,
        });
        toast('Booking saved', 'success');
      } else {
        detail = await adminApi.createBooking({ ...common, hirePrice });
        toast(`Booking ${detail.reference} created`, 'success');
      }
      render();
      await options.onSaved();
    } catch (e) {
      showError(modal.body, e, reload);
    } finally {
      saving = false;
    }
  };

  // ---- Add-ons -----------------------------------------------------------------------------------------------------

  const addonsSection = (b: BookingDetail): HTMLElement => {
    const section = h('section', { class: 'panel', 'aria-labelledby': 'addons-heading' }, h('h3', { id: 'addons-heading' }, 'Add-ons'));
    if (b.addonLines.length === 0) section.append(h('p', { class: 'muted' }, 'No add-ons.'));
    else {
      const table = h('table', { class: 'table table--compact' },
        h('thead', {}, h('tr', {}, h('th', {}, 'Add-on'), h('th', {}, 'Qty'), h('th', {}, 'Unit price'), h('th', {}, 'Status'), h('th', {}, h('span', { class: 'visually-hidden' }, 'Actions')))));
      const body = h('tbody');
      for (const line of b.addonLines) {
        const qty = h('input', { type: 'number', min: 1, value: line.quantity, class: 'input-sm', 'aria-label': `Quantity of ${line.name}` });
        const price = h('input', { type: 'number', min: 0, step: '0.01', value: line.unitPrice ?? '', placeholder: 'On request', class: 'input-sm', 'aria-label': `Unit price of ${line.name}` });
        const status = h('select', { class: 'input-sm', 'aria-label': `Status of ${line.name}` },
          ...(['Requested', 'Confirmed', 'Declined'] as AddonLineStatus[]).map((s) => h('option', { value: s, selected: s === line.status }, s)));
        body.append(h('tr', {},
          h('td', {}, line.name),
          h('td', {}, qty),
          h('td', {}, price),
          h('td', {}, status),
          h('td', { class: 'nowrap' },
            h('button', { type: 'button', class: 'button button--small', onclick: () => void run(section, () => adminApi.updateAddonLine(b.id, line.id, {
              version: b.version, quantity: Number(qty.value) || 1, unitPrice: numberOrNull(price.value), status: status.value as AddonLineStatus,
            }), 'Add-on updated') }, 'Save'),
            h('button', { type: 'button', class: 'button button--small button--danger', onclick: () => void run(section, () => adminApi.removeAddonLine(b.id, line.id, b.version)) }, 'Remove'))));
      }
      table.append(body);
      section.append(table, h('p', { class: 'field__hint' }, 'Confirming or declining sends no email — please contact the customer.'));
    }
    const choose = h('select', { class: 'input-sm', 'aria-label': 'Add-on to add' },
      ...addons.filter((a) => a.isActive).map((a) => h('option', { value: a.id }, a.name)));
    const qty = h('input', { type: 'number', min: 1, value: 1, class: 'input-sm', 'aria-label': 'Quantity' });
    section.append(h('div', { class: 'inline-form' }, choose, qty,
      h('button', { type: 'button', class: 'button button--small', onclick: () => void run(section, () => adminApi.addAddonLine(b.id, b.version, choose.value, Number(qty.value) || 1), 'Add-on added') }, 'Add add-on')));
    return section;
  };

  // ---- Payments ----------------------------------------------------------------------------------------------------

  const paymentsSection = (b: BookingDetail): HTMLElement => {
    const section = h('section', { class: 'panel', 'aria-labelledby': 'payments-heading' }, h('h3', { id: 'payments-heading' }, 'Payments'));
    section.append(h('dl', { class: 'totals' },
      h('dt', {}, 'Total'), h('dd', {}, formatMoney(b.totalPrice)),
      h('dt', {}, 'Paid'), h('dd', {}, formatMoney(b.amountPaid)),
      h('dt', {}, 'Owing'), h('dd', {}, formatMoney(b.amountOwing)),
      h('dt', {}, 'Payment status'), h('dd', {}, h('span', { class: `pay-pill pay-pill--${b.paymentStatus}` }, PAYMENT_STATUS_LABEL[b.paymentStatus] ?? b.paymentStatus)),
      h('dt', {}, 'Due now'), h('dd', {}, formatMoney(b.amountDueNow)),
      h('dt', {}, 'Schedule'), h('dd', {}, `${b.paymentSchedule}`)));
    section.append(h('ul', { class: 'plain-list schedule' }, ...b.milestones.map((m) =>
      h('li', {}, `${m.label}: ${formatMoney(m.amount)}${m.dueDate ? ` by ${formatDate(m.dueDate)}` : ''}`))));

    if (b.payments.length > 0) {
      section.append(h('table', { class: 'table table--compact' },
        h('thead', {}, h('tr', {}, h('th', {}, 'Date'), h('th', {}, 'Amount'), h('th', {}, 'Method'), h('th', {}, 'Note'))),
        h('tbody', {}, ...b.payments.map((p) => h('tr', {},
          h('td', {}, formatInstant(p.paidAt)), h('td', {}, formatMoney(p.amount)),
          h('td', {}, p.method === 'Stripe' ? 'Stripe' : 'Manual'), h('td', {}, p.note ?? ''))))));
    } else {
      section.append(h('p', { class: 'muted' }, 'No payments yet.'));
    }

    const amount = h('input', { type: 'number', min: 0.01, step: '0.01', class: 'input-sm', placeholder: 'Amount', 'aria-label': 'Payment amount' });
    const date = h('input', { type: 'date', class: 'input-sm', value: sydneyToday(), 'aria-label': 'Payment date' });
    const note = h('input', { type: 'text', class: 'input-sm', placeholder: 'Note (e.g. bank transfer)', 'aria-label': 'Payment note' });
    section.append(
      h('h4', {}, 'Add manual payment'),
      h('div', { class: 'inline-form' }, amount, date, note,
        h('button', { type: 'button', class: 'button button--small', onclick: () => void run(section, () => adminApi.addPayment(b.id, {
          version: b.version, amount: Number(amount.value), date: date.value, note: note.value.trim() || null,
        }), 'Payment recorded') }, 'Add payment')),
      h('div', { class: 'inline-form' },
        h('button', {
          type: 'button', class: 'button', disabled: b.amountDueNow <= 0 || b.status !== 'Active' || b.isStandby,
          onclick: async () => {
            try {
              const result = await adminApi.sendPaymentLink(b.id);
              toast(`Payment link for ${formatMoney(result.amountDue)} sent to ${result.sentTo}`, 'success');
            } catch (e) {
              showError(section, e);
            }
          },
        }, 'Send payment link')),
    );
    return section;
  };

  const onlineSection = (b: BookingDetail): HTMLElement =>
    h('section', { class: 'panel', 'aria-labelledby': 'online-heading' },
      h('h3', { id: 'online-heading' }, 'Online booking'),
      h('dl', { class: 'totals' },
        h('dt', {}, 'Created by'), h('dd', {}, 'Customer (website)'),
        h('dt', {}, 'Created'), h('dd', {}, formatInstant(b.createdDate)),
        h('dt', {}, 'Terms accepted'), h('dd', {}, formatInstant(b.termsAcceptedAt)),
        h('dt', {}, 'Rooming warning'), h('dd', {}, b.roomingWarningAcceptedAt ? `Accepted ${formatInstant(b.roomingWarningAcceptedAt)}` : 'Not required'),
        h('dt', {}, 'Group restriction'), h('dd', {}, b.groupRestrictionDeclaredNotApplicable ? 'Declared not under-30s / all-male' : '—')),
      b.roomingWarningText ? h('blockquote', { class: 'quote' }, b.roomingWarningText) : null,
      b.holdExpiresAt ? h('p', { class: 'muted' }, `Hold expires ${formatInstant(b.holdExpiresAt)}`) : null);

  render();
}
