import { addDays, formatDate, formatTime, formatTimestamp, sydneyToday } from '@shared/dates';
import { clear, h, toast } from '@shared/dom';
import { formatMoney } from '@shared/format';
import { formatPhone, phoneField, type PhoneField } from '@shared/phone';
import {
  adminApi, type Addon, type AdminAddonLineRequest, type BoatSummary, type BookingDetail, type BookingStatus, type PeriodType,
  type Rebook,
} from '../api';
import { clearErrors, field, numberOrNull, openModal, PAYMENT_STATUS_LABEL, PERIOD_LABEL, showError, STATUS_LABEL, value } from '../ui';

interface Options {
  id?: string;
  boatId?: string;
  startDate?: string;
  /** Re-book: a new booking pre-filled from an earlier one. */
  rebook?: Rebook;
  onSaved: () => Promise<void> | void;
}

const NIGHTS: Partial<Record<PeriodType, number>> = { Midweek: 4, Weekend: 3, Week: 7, LongWeekend: 4 };
const STANDARD_CHECK_IN = '11:00';
const STANDARD_CHECK_OUT = '08:00';

/** "HH:mm" from an API time that may carry seconds. */
const hhmm = (time: string | null | undefined) => (time ?? '').slice(0, 5);

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

  const rebook = detail ? undefined : options.rebook;
  /** Add-on lines a new booking will be created with (Re-book copies them). */
  const pendingLines: (AdminAddonLineRequest & { name: string })[] = (rebook?.addons ?? []).map((a) => ({ ...a }));
  let priceTouched = !!detail || rebook?.hirePrice != null;
  let saving = false;
  let phone: PhoneField;
  let secondaryPhone: PhoneField;

  const render = () => {
    clear(modal.body);
    clear(modal.footer);
    modal.setTitle(detail ? `Booking ${detail.reference}` : rebook ? `Re-book ${rebook.originalReference}` : 'New booking');
    const form = h('form', { class: 'booking-form', novalidate: true });
    const left = h('div', { class: 'booking-form__main' });
    const right = h('div', { class: 'booking-form__side' });
    form.append(left, right);
    modal.body.append(form);

    const editable = !detail || detail.status === 'Active' || detail.status === 'Cancelled';
    const boatOptions = boats
      .filter((b) => b.type === 'House' && (b.isActive || b.id === detail?.boatId))
      .map((b) => ({ value: b.id, label: b.isActive ? b.name : `${b.name} (inactive)` }));

    const start = detail?.startDate ?? rebook?.startDate ?? options.startDate ?? addDays(sydneyToday(), 30);
    const period: PeriodType = detail?.periodType ?? rebook?.periodType ?? 'Custom';
    const contact = detail?.customer ?? (detail?.customerDetails
      ? { email: detail.customerDetails.email, fullName: detail.customerDetails.fullName, mobileNumber: detail.customerDetails.mobileNumber, secondaryPhone: null }
      : rebook ? { email: rebook.email, fullName: rebook.fullName, mobileNumber: rebook.phone, secondaryPhone: rebook.secondaryPhone } : null);
    phone = phoneField({ name: 'phone', label: 'Phone', value: contact?.mobileNumber });
    secondaryPhone = phoneField({ name: 'secondaryPhone', label: 'Secondary phone', value: contact?.secondaryPhone });

    if (detail) {
      left.append(h('p', { class: 'booking-meta' },
        h('span', { class: `status-pill status-pill--${detail.status}` }, STATUS_LABEL[detail.status] ?? detail.status),
        ` ${detail.isStandby ? 'Stand-by · ' : ''}Created ${formatTimestamp(detail.createdDate)} by ${detail.createdBy === 'Customer' ? 'customer (website)' : 'staff'}`));
    } else if (rebook) {
      left.append(h('div', { class: 'notice' }, `Pre-filled from ${rebook.originalReference}. Check the dates and prices before creating the booking.`));
    }
    if (detail && detail.warnings.length > 0) {
      left.append(h('div', { class: 'notice notice--warning', role: 'status' },
        h('ul', { class: 'plain-list' }, ...detail.warnings.map((w) => h('li', {}, w)))));
    }

    left.append(
      h('div', { class: 'form-grid' },
        field({ label: 'Boat', name: 'boatId', required: true, value: detail?.boatId ?? rebook?.boatId ?? options.boatId ?? boatOptions[0]?.value, options: boatOptions }),
        field({ label: 'Period type', name: 'periodType', required: true, value: period,
          options: Object.entries(PERIOD_LABEL).map(([v, l]) => ({ value: v, label: l })) }),
        field({ label: 'Start date', name: 'startDate', type: 'date', required: true, value: start }),
        field({ label: 'End date', name: 'endDate', type: 'date', required: true, value: detail?.endDate ?? rebook?.endDate ?? addDays(start, NIGHTS[period] ?? 1) }),
        field({ label: 'Number of adults', name: 'numberOfAdults', type: 'number', required: true,
          value: detail?.numberOfAdults ?? rebook?.numberOfAdults ?? 2, attrs: { min: 1, max: 99 } }),
        field({ label: 'Number of children (under 12)', name: 'numberOfChildren', type: 'number', required: true,
          value: detail?.numberOfChildren ?? rebook?.numberOfChildren ?? 0, attrs: { min: 0, max: 99 } }),
        field({ label: 'Check-in time', name: 'checkInTime', type: 'time', required: true,
          value: hhmm(detail?.checkInTime ?? rebook?.checkInTime) || STANDARD_CHECK_IN, attrs: { step: 300 } }),
        field({ label: 'Check-out time', name: 'checkOutTime', type: 'time', required: true,
          value: hhmm(detail?.checkOutTime ?? rebook?.checkOutTime) || STANDARD_CHECK_OUT, attrs: { step: 300 } }),
      ),
      h('p', { class: 'field__hint' }, 'Times are for staff only (standard: check-in 11am – 2pm, check-out by 8am) and never affect availability.'),
      h('div', { class: 'notice notice--warning form-warning', id: 'guest-warning', 'aria-live': 'polite', hidden: true }),
      h('h3', {}, 'Customer'),
      h('div', { class: 'form-grid' },
        field({ label: 'Email', name: 'email', type: 'email', required: true, value: contact?.email, hint: 'Existing customers are filled in automatically.' }),
        field({ label: 'Full name', name: 'fullName', required: true, value: contact?.fullName }),
        phone.element,
        secondaryPhone.element,
      ),
      h('div', { class: 'form-grid' },
        h('div', { class: 'field', 'data-field': 'isStandby' },
          h('span', { class: 'field__label' }, 'Stand-by'),
          h('label', { class: 'toggle toggle--field' }, h('input', { type: 'checkbox', name: 'isStandby', checked: detail?.isStandby ?? false }), ' Stand-by booking'),
          h('span', { class: 'field__hint' }, 'Stand-bys never block dates, get no emails and are superseded by a real booking.')),
        detail && !editable
          ? h('div', { class: 'field' }, h('span', { class: 'field__label' }, 'Status'), h('span', { class: 'field__static' }, STATUS_LABEL[detail.status] ?? detail.status))
          : field({ label: 'Status', name: 'status', value: detail?.status ?? 'Active', options: [
            { value: 'Active', label: 'Active' }, { value: 'Cancelled', label: 'Cancelled' }] }),
      ),
      field({ label: 'Comments', name: 'comments', textarea: true, value: detail?.comments }),
      h('div', { class: 'price-row' },
        field({ label: 'Hire price (AUD, incl. GST)', name: 'hirePrice', type: 'number', required: true, value: detail?.hirePrice ?? rebook?.hirePrice ?? '', attrs: { min: 0, step: '0.01' } }),
        detail ? h('button', { type: 'button', class: 'button', id: 'recalculate', disabled: !editable }, 'Recalculate from rates') : null,
        h('span', { class: 'field__hint', id: 'price-hint' })),
    );

    if (detail && !editable) {
      const contactNames = detail.customer ? ['email', 'fullName', 'phone', 'secondaryPhone'] : [];
      left.querySelectorAll<HTMLInputElement>('input, select').forEach((el) => {
        const name = el.closest<HTMLElement>('[data-field]')?.dataset.field ?? el.name;
        if (name !== 'comments' && !contactNames.includes(name)) el.disabled = true;
      });
      left.prepend(h('div', { class: 'notice notice--warning' }, detail.customer
        ? `This booking is ${STATUS_LABEL[detail.status]}; only comments and customer details can be changed.`
        : `This booking is ${STATUS_LABEL[detail.status]} and has no customer record; only comments can be changed.`));
    }

    if (detail) {
      right.append(addonsSection(detail), paymentsSection(detail));
      if (detail.createdBy === 'Customer') right.append(onlineSection(detail));
    } else {
      right.append(newAddonsSection(form));
      right.append(h('p', { class: 'muted' }, 'Payments can be added after the booking is saved.'));
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

    priceIn.addEventListener('input', () => (priceTouched = true));
    for (const name of ['numberOfAdults', 'numberOfChildren', 'boatId']) {
      form.querySelector(`[name="${name}"]`)!.addEventListener('input', () => updateGuestWarning(form));
    }

    // Selecting a period type pre-fills the end date; for new bookings the price is pre-filled from the rates.
    const prefill = async (fillEnd = true) => {
      const periodType = periodSel.value as PeriodType;
      const nights = NIGHTS[periodType];
      if (fillEnd && nights && startIn.value) endIn.value = addDays(startIn.value, nights);
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
    if (!detail) void prefill(!rebook);

    emailIn.addEventListener('blur', async () => {
      if (!emailIn.value.includes('@') || emailIn.disabled) return;
      try {
        const customer = await adminApi.customerByEmail(emailIn.value.trim());
        const name = form.querySelector<HTMLInputElement>('[name="fullName"]')!;
        if (!name.value) name.value = customer.fullName;
        if (!phone.numberInput.value && customer.mobileNumber) phone.setValue(customer.mobileNumber);
        if (!secondaryPhone.numberInput.value && customer.secondaryPhone) secondaryPhone.setValue(customer.secondaryPhone);
        toast(`Existing customer: ${customer.fullName}`);
      } catch {
        // new customer
      }
    });

    form.querySelector('#recalculate')?.addEventListener('click', () => void run(form, () => adminApi.recalculatePrice(detail!.id, detail!.version)));
  };

  /** Non-blocking warnings: guests above the boat's maximum or above adult beds + kids beds. */
  const updateGuestWarning = (form: HTMLElement) => {
    const warning = form.querySelector<HTMLElement>('#guest-warning');
    if (!warning) return;
    const boat = boats.find((b) => b.id === value(form, 'boatId'));
    const guests = (Number(value(form, 'numberOfAdults')) || 0) + (Number(value(form, 'numberOfChildren')) || 0);
    const beds = boat?.adultBeds != null ? boat.adultBeds + (boat.kidsBeds ?? 0) : null;
    let message = '';
    if (boat && guests > boat.maxNoOfGuests) message = `${guests} guests is more than the boat's maximum of ${boat.maxNoOfGuests}.`;
    else if (beds !== null && guests > beds) message = `${guests} guests but only ${beds} beds (${boat!.adultBeds} adult, ${boat!.kidsBeds ?? 0} kids); some will share.`;
    warning.textContent = message;
    warning.hidden = !message;
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
    const phoneValue = phone.value();
    const secondaryValue = secondaryPhone.value();
    const invalid = [phoneValue === undefined ? phone : null, secondaryValue === undefined ? secondaryPhone : null].filter((p) => p !== null);
    if (invalid.length > 0) {
      for (const p of invalid) {
        p.element.classList.add('field--invalid');
        p.element.append(h('span', { class: 'field__error' }, 'Enter a valid number for the selected country.'));
      }
      return;
    }
    const hirePrice = numberOrNull(value(form, 'hirePrice'));
    const common = {
      boatId: value(form, 'boatId'),
      periodType: value(form, 'periodType') as PeriodType,
      startDate: value(form, 'startDate'),
      endDate: value(form, 'endDate') || null,
      numberOfAdults: Number(value(form, 'numberOfAdults')) || 0,
      numberOfChildren: Number(value(form, 'numberOfChildren')) || 0,
      checkInTime: value(form, 'checkInTime') || STANDARD_CHECK_IN,
      checkOutTime: value(form, 'checkOutTime') || STANDARD_CHECK_OUT,
      email: value(form, 'email') || null,
      fullName: value(form, 'fullName') || null,
      phone: phoneValue ?? null,
      secondaryPhone: secondaryValue ?? null,
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
        detail = await adminApi.createBooking({
          ...common,
          hirePrice,
          addons: pendingLines.map(({ addonId, quantity, unitPrice }) => ({ addonId, quantity, unitPrice })),
        });
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

  const addonPicker = () => h('select', { class: 'input-sm', 'aria-label': 'Add-on to add' },
    ...addons.filter((a) => a.isActive).map((a) => h('option', { value: a.id }, a.name)));

  /** Shows what is left of a capacity-limited add-on for the dates (excluding this booking). */
  const capacityHint = (picker: HTMLSelectElement, hint: HTMLElement, dates: () => { start: string; end: string } | null, excludeId?: string) => {
    const update = async () => {
      const addon = addons.find((a) => a.id === picker.value);
      const range = dates();
      hint.textContent = '';
      if (!addon?.capacity || !range || range.end <= range.start) return;
      try {
        const result = await adminApi.addonAvailability(addon.id, range.start, range.end, excludeId);
        const unit = addon.capacityUnitLabel ? ` ${addon.capacityUnitLabel}` : '';
        hint.textContent = `${result.remaining} of ${result.capacity}${unit} left for these dates.`;
      } catch {
        // optional hint
      }
    };
    picker.addEventListener('change', () => void update());
    void update();
  };

  /** New booking: the lines it will be created with (e.g. copied by Re-book), editable before saving. */
  const newAddonsSection = (form: HTMLFormElement): HTMLElement => {
    const section = h('section', { class: 'panel', 'aria-labelledby': 'addons-heading' }, h('h3', { id: 'addons-heading' }, 'Add-ons'));
    const list = h('div', {});
    const draw = () => {
      clear(list);
      if (pendingLines.length === 0) {
        list.append(h('p', { class: 'muted' }, 'No add-ons.'));
        return;
      }
      list.append(h('table', { class: 'table table--compact' },
        h('thead', {}, h('tr', {}, h('th', {}, 'Add-on'), h('th', {}, 'Qty'), h('th', {}, 'Unit price'), h('th', {}, h('span', { class: 'visually-hidden' }, 'Actions')))),
        h('tbody', {}, ...pendingLines.map((line, index) => {
          const qty = h('input', { type: 'number', min: 1, value: line.quantity, class: 'input-sm', 'aria-label': `Quantity of ${line.name}` });
          qty.addEventListener('input', () => (line.quantity = Number(qty.value) || 1));
          const price = h('input', { type: 'number', min: 0, step: '0.01', value: line.unitPrice ?? '', placeholder: 'Current price', class: 'input-sm', 'aria-label': `Unit price of ${line.name}` });
          price.addEventListener('input', () => (line.unitPrice = numberOrNull(price.value)));
          return h('tr', {}, h('td', {}, line.name), h('td', {}, qty), h('td', {}, price),
            h('td', {}, h('button', { type: 'button', class: 'button button--small button--danger', onclick: () => {
              pendingLines.splice(index, 1);
              draw();
            } }, 'Remove')));
        }))));
    };
    draw();
    const choose = addonPicker();
    const qty = h('input', { type: 'number', min: 1, value: 1, class: 'input-sm', 'aria-label': 'Quantity' });
    const hint = h('p', { class: 'field__hint', 'aria-live': 'polite' });
    capacityHint(choose, hint, () => ({ start: value(form, 'startDate'), end: value(form, 'endDate') }));
    section.append(list,
      h('div', { class: 'inline-form' }, choose, qty,
        h('button', { type: 'button', class: 'button button--small', onclick: () => {
          const addon = addons.find((a) => a.id === choose.value);
          if (!addon) return;
          pendingLines.push({ addonId: addon.id, name: addon.name, quantity: addon.quantityApplies ? Number(qty.value) || 1 : 1, unitPrice: null });
          draw();
        } }, 'Add add-on')),
      hint,
      h('p', { class: 'field__hint' }, 'An empty unit price uses the current price for the period type (none for long weekends and custom stays).'));
    return section;
  };

  const addonsSection = (b: BookingDetail): HTMLElement => {
    const section = h('section', { class: 'panel', 'aria-labelledby': 'addons-heading' }, h('h3', { id: 'addons-heading' }, 'Add-ons'));
    if (b.addonLines.length === 0) section.append(h('p', { class: 'muted' }, 'No add-ons.'));
    else {
      const table = h('table', { class: 'table table--compact' },
        h('thead', {}, h('tr', {}, h('th', {}, 'Add-on'), h('th', {}, 'Qty'), h('th', {}, 'Unit price'), h('th', {}, 'Total'), h('th', {}, h('span', { class: 'visually-hidden' }, 'Actions')))));
      const body = h('tbody');
      for (const line of b.addonLines) {
        const qty = h('input', { type: 'number', min: 1, value: line.quantity, class: 'input-sm', 'aria-label': `Quantity of ${line.name}` });
        const price = h('input', { type: 'number', min: 0, step: '0.01', value: line.unitPrice ?? '', placeholder: 'Enter price', class: 'input-sm', 'aria-label': `Unit price of ${line.name}` });
        const over = line.remaining !== null && line.quantity > line.remaining;
        body.append(h('tr', { class: line.unitPrice === null ? 'row--attention' : '' },
          h('td', {}, line.name,
            line.capacity !== null
              ? h('span', { class: `field__hint addon-capacity${over ? ' addon-capacity--over' : ''}` },
                `${line.remaining} of ${line.capacity} left for these dates${over ? ' — over capacity' : ''}`)
              : null),
          h('td', {}, qty),
          h('td', {}, price),
          h('td', { class: 'nowrap' }, line.lineTotal !== null ? formatMoney(line.lineTotal) : 'Not priced'),
          h('td', { class: 'nowrap' },
            h('button', { type: 'button', class: 'button button--small', onclick: () => void run(section, () => adminApi.updateAddonLine(b.id, line.id, {
              version: b.version, quantity: Number(qty.value) || 1, unitPrice: numberOrNull(price.value),
            }), 'Add-on updated') }, 'Save'),
            h('button', { type: 'button', class: 'button button--small button--danger', onclick: () => void run(section, () => adminApi.removeAddonLine(b.id, line.id, b.version), 'Add-on removed') }, 'Remove'))));
      }
      table.append(body);
      section.append(table, h('p', { class: 'field__hint' },
        'A line on the booking is accepted; remove an add-on to decline it. Lines without a price don\'t count toward the total. No email is sent — please contact the customer.'));
    }
    const choose = addonPicker();
    const qty = h('input', { type: 'number', min: 1, value: 1, class: 'input-sm', 'aria-label': 'Quantity' });
    const hint = h('p', { class: 'field__hint', 'aria-live': 'polite' });
    capacityHint(choose, hint, () => ({ start: b.startDate, end: b.endDate }), b.id);
    section.append(h('div', { class: 'inline-form' }, choose, qty,
      h('button', { type: 'button', class: 'button button--small', onclick: () => void run(section, () => adminApi.addAddonLine(b.id, b.version, choose.value, Number(qty.value) || 1), 'Add-on added') }, 'Add add-on')),
    hint);
    return section;
  };

  // ---- Payments ----------------------------------------------------------------------------------------------------

  const paymentsSection = (b: BookingDetail): HTMLElement => {
    const section = h('section', { class: 'panel', 'aria-labelledby': 'payments-heading' }, h('h3', { id: 'payments-heading' }, 'Payments'));
    section.append(h('dl', { class: 'totals' },
      h('dt', {}, 'Total'), h('dd', {}, formatMoney(b.totalPrice)),
      h('dt', {}, 'Deposit'), h('dd', {}, formatMoney(b.depositAmount)),
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
          h('td', {}, formatTimestamp(p.paidAt)), h('td', {}, formatMoney(p.amount)),
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
        h('dt', {}, 'Created'), h('dd', {}, formatTimestamp(b.createdDate)),
        h('dt', {}, 'Terms accepted'), h('dd', {}, formatTimestamp(b.termsAcceptedAt) || '—'),
        h('dt', {}, 'Rooming warning'), h('dd', {}, b.roomingWarningAcceptedAt ? `Accepted ${formatTimestamp(b.roomingWarningAcceptedAt)}` : 'Not required'),
        ...(b.customerDetails ? [
          h('dt', {}, 'Entered online'),
          h('dd', {}, `${b.customerDetails.fullName} · ${b.customerDetails.email} · ${formatPhone(b.customerDetails.mobileNumber)}`),
        ] : []),
        h('dt', {}, 'Times'), h('dd', {}, `Check-in ${formatTime(hhmm(b.checkInTime))}, check-out ${formatTime(hhmm(b.checkOutTime))}`)),
      b.roomingWarningText ? h('blockquote', { class: 'quote' }, b.roomingWarningText) : null,
      b.holdExpiresAt ? h('p', { class: 'muted' }, `Hold expires ${formatTimestamp(b.holdExpiresAt)}`) : null);

  render();
}
