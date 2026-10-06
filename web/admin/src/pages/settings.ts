import { formatDate } from '@shared/dates';
import { clear, h, openDialog, toast } from '@shared/dom';
import { formatMoney } from '@shared/format';
import { adminApi, type Addon, type BlockedPeriod, type EmailTemplateKey, type Season } from '../api';
import { clearErrors, field, loading, numberOrNull, openModal, showError, value } from '../ui';

const TEMPLATE_LABELS: Record<string, string> = {
  BookingConfirmed: '#1 Booking confirmed (customer)',
  StaffNewOnlineBooking: '#2 New online booking (staff)',
  PaymentDue: '#4 Payment due, with link (customer)',
  PaymentReceived: '#5 Payment received (customer)',
  PreHireInstructions: '#6 Pre-hire instructions (customer)',
  StaffBalanceOverdue: '#7 Overdue balance (staff)',
  StaffStandbySuperseded: '#8 Stand-by superseded (staff)',
};

export async function renderSettings(page: HTMLElement): Promise<void> {
  const sections = [
    { id: 'general', title: 'General', render: general },
    { id: 'seasons', title: 'Seasons', render: seasons },
    { id: 'blocked', title: 'Blocked periods', render: blockedPeriods },
    { id: 'addons', title: 'Add-ons', render: addons },
    { id: 'templates', title: 'Email templates', render: templates },
  ];
  const nav = h('nav', { class: 'settings-nav', 'aria-label': 'Settings sections' },
    ...sections.map((s) => h('a', { href: `#/settings/${s.id}` }, s.title)));
  page.append(h('div', { class: 'toolbar' }, h('h1', { class: 'toolbar__title' }, 'Settings')), nav);
  for (const s of sections) {
    const body = h('div', {});
    page.append(h('section', { class: 'panel settings-section', id: `settings-${s.id}`, 'aria-labelledby': `h-${s.id}` },
      h('h2', { id: `h-${s.id}` }, s.title), body));
    void s.render(body);
  }
  const target = location.hash.split('/')[2];
  if (target) setTimeout(() => document.getElementById(`settings-${target}`)?.scrollIntoView(), 300);
}

// ---- General -----------------------------------------------------------------------------------------------------

async function general(body: HTMLElement): Promise<void> {
  loading(body);
  try {
    const s = await adminApi.business();
    clear(body);
    const form = h('div', {},
      h('div', { class: 'form-grid' },
        field({ label: 'Deposit (AUD)', name: 'depositAmount', type: 'number', required: true, value: s.depositAmount, attrs: { min: 1, step: '0.01' } }),
        field({ label: 'Minimum lead time (days)', name: 'minimumLeadTimeDays', type: 'number', required: true, value: s.minimumLeadTimeDays, attrs: { min: 0 } }),
        field({ label: 'Bookings open until', name: 'bookingsOpenUntil', type: 'date', required: true, value: s.bookingsOpenUntil,
          hint: 'The last night customers can book online. Move it forward each year after entering blocked periods and prices.' }),
        field({ label: 'Staff alert email', name: 'staffAlertEmail', type: 'email', required: true, value: s.staffAlertEmail }),
        field({ label: 'Contact phone', name: 'contactPhone', required: true, value: s.contactPhone }),
        field({ label: 'Contact email', name: 'contactEmail', type: 'email', required: true, value: s.contactEmail }),
      ),
      field({ label: 'Hire terms URL', name: 'hireTermsUrl', type: 'url', required: true, value: s.hireTermsUrl }));
    let version = s.version;
    body.append(form, h('button', { type: 'button', class: 'button button--primary', onclick: async () => {
      clearErrors(form);
      try {
        const saved = await adminApi.updateBusiness({
          version,
          depositAmount: Number(value(form, 'depositAmount')),
          minimumLeadTimeDays: Number(value(form, 'minimumLeadTimeDays')),
          bookingsOpenUntil: value(form, 'bookingsOpenUntil'),
          staffAlertEmail: value(form, 'staffAlertEmail'),
          contactPhone: value(form, 'contactPhone'),
          contactEmail: value(form, 'contactEmail'),
          hireTermsUrl: value(form, 'hireTermsUrl'),
        });
        version = saved.version;
        toast('Settings saved', 'success');
      } catch (e) {
        showError(form, e, () => void general(body));
      }
    } }, 'Save general settings'));
  } catch (e) {
    clear(body);
    showError(body, e);
  }
}

// ---- Seasons -----------------------------------------------------------------------------------------------------

async function seasons(body: HTMLElement): Promise<void> {
  loading(body);
  let list: Season[];
  try {
    list = await adminApi.seasons();
  } catch (e) {
    clear(body);
    showError(body, e);
    return;
  }
  clear(body);
  const refresh = () => void seasons(body);
  body.append(
    h('p', { class: 'field__hint' }, 'Recurring day-month ranges (dd-MM) used for pricing. Ranges may wrap the year end (e.g. 01-12 → 31-01) and must not overlap. Dates not covered fall in Normal.'),
    h('table', { class: 'table' },
      h('thead', {}, h('tr', {}, h('th', {}, 'Season'), h('th', {}, 'Ranges'), h('th', {}, h('span', { class: 'visually-hidden' }, 'Actions')))),
      h('tbody', {}, ...list.map((s) => h('tr', {},
        h('td', {}, s.name, s.isDefault ? h('span', { class: 'muted' }, ' (default)') : ''),
        h('td', {}, s.isDefault ? 'Everything else' : s.ranges.map((r) => `${r.startDayMonth} → ${r.endDayMonth}`).join(', ')),
        h('td', { class: 'nowrap' }, s.isDefault ? '' : h('button', { type: 'button', class: 'button button--small', onclick: () => editSeason(s, refresh) }, 'Edit'),
          s.isDefault ? '' : h('button', { type: 'button', class: 'button button--small button--danger', onclick: async () => {
            if (await confirmDelete(`season ${s.name}`)) {
              try {
                await adminApi.deleteSeason(s.id, s.version);
                refresh();
              } catch (e) {
                showError(body, e);
              }
            }
          } }, 'Delete')))))),
    h('button', { type: 'button', class: 'button', onclick: () => editSeason(null, refresh) }, '+ New season'));
}

function editSeason(season: Season | null, refresh: () => void): void {
  const modal = openModal(season ? `Edit ${season.name}` : 'New season');
  const rows = h('div', { class: 'ranges' });
  const addRow = (start = '', end = '') => {
    const row = h('div', { class: 'inline-form range-row' },
      h('input', { type: 'text', class: 'input-sm', value: start, placeholder: 'dd-MM', 'aria-label': 'Start (dd-MM)', 'data-range': 'start', pattern: '\\d{2}-\\d{2}' }),
      '→',
      h('input', { type: 'text', class: 'input-sm', value: end, placeholder: 'dd-MM', 'aria-label': 'End (dd-MM)', 'data-range': 'end', pattern: '\\d{2}-\\d{2}' }),
      h('button', { type: 'button', class: 'button button--small', onclick: () => row.remove() }, 'Remove'));
    rows.append(row);
  };
  for (const r of season?.ranges ?? [{ startDayMonth: '', endDayMonth: '' }]) addRow(r.startDayMonth, r.endDayMonth);
  const form = h('div', {}, field({ label: 'Name', name: 'name', required: true, value: season?.name }),
    h('div', { class: 'field', 'data-field': 'ranges' }, h('span', { class: 'field__label' }, 'Ranges'), rows,
      h('button', { type: 'button', class: 'button button--small', onclick: () => addRow() }, '+ Add range')));
  modal.body.append(form);
  modal.footer.append(h('button', { type: 'button', class: 'button', onclick: () => modal.close() }, 'Cancel'),
    h('button', { type: 'button', class: 'button button--primary', onclick: async () => {
      const ranges = Array.from(rows.querySelectorAll('.range-row')).map((row) => ({
        startDayMonth: row.querySelector<HTMLInputElement>('[data-range="start"]')!.value.trim(),
        endDayMonth: row.querySelector<HTMLInputElement>('[data-range="end"]')!.value.trim(),
      }));
      try {
        const request = { version: season?.version ?? 0, name: value(form, 'name'), ranges };
        if (season) await adminApi.updateSeason(season.id, request);
        else await adminApi.createSeason(request);
        modal.close();
        toast('Season saved', 'success');
        refresh();
      } catch (e) {
        showError(form, e);
      }
    } }, 'Save'));
}

// ---- Blocked periods ---------------------------------------------------------------------------------------------

async function blockedPeriods(body: HTMLElement): Promise<void> {
  loading(body);
  let list: BlockedPeriod[];
  try {
    list = await adminApi.blockedPeriods();
  } catch (e) {
    clear(body);
    showError(body, e);
    return;
  }
  clear(body);
  const refresh = () => void blockedPeriods(body);
  const edit = (period: BlockedPeriod | null) => {
    const modal = openModal(period ? 'Edit blocked period' : 'New blocked period');
    const form = h('div', {},
      field({ label: 'Name', name: 'name', required: true, value: period?.name, hint: 'Shown to customers, e.g. "Easter 2027" or "Christmas / New Year 2027–28".' }),
      h('div', { class: 'form-grid' },
        field({ label: 'First night', name: 'firstNight', type: 'date', required: true, value: period?.firstNight }),
        field({ label: 'Last night', name: 'lastNight', type: 'date', required: true, value: period?.lastNight })),
      h('label', { class: 'toggle' }, h('input', { type: 'checkbox', name: 'usesExtendedPaymentSchedule', checked: period?.usesExtendedPaymentSchedule ?? false }),
        ' Extended payment schedule (50% 120 days before, 100% 90 days before) — Christmas/New Year only'));
    modal.body.append(form);
    modal.footer.append(h('button', { type: 'button', class: 'button', onclick: () => modal.close() }, 'Cancel'),
      h('button', { type: 'button', class: 'button button--primary', onclick: async () => {
        const request = {
          version: period?.version ?? 0,
          name: value(form, 'name'),
          firstNight: value(form, 'firstNight'),
          lastNight: value(form, 'lastNight'),
          usesExtendedPaymentSchedule: form.querySelector<HTMLInputElement>('[name="usesExtendedPaymentSchedule"]')!.checked,
        };
        try {
          if (period) await adminApi.updateBlockedPeriod(period.id, request);
          else await adminApi.createBlockedPeriod(request);
          modal.close();
          toast('Blocked period saved', 'success');
          refresh();
        } catch (e) {
          showError(form, e);
        }
      } }, 'Save'));
  };
  body.append(
    h('p', { class: 'notice' }, "Enter each year's Christmas/New Year, Easter and long weekends. Customers must phone to book these."),
    h('table', { class: 'table' },
      h('thead', {}, h('tr', {}, h('th', {}, 'Name'), h('th', {}, 'First night'), h('th', {}, 'Last night'), h('th', {}, 'Extended schedule'), h('th', {}, h('span', { class: 'visually-hidden' }, 'Actions')))),
      h('tbody', {}, ...list.map((p) => h('tr', {},
        h('td', {}, p.name), h('td', {}, formatDate(p.firstNight)), h('td', {}, formatDate(p.lastNight)),
        h('td', {}, p.usesExtendedPaymentSchedule ? 'Yes' : 'No'),
        h('td', { class: 'nowrap' },
          h('button', { type: 'button', class: 'button button--small', onclick: () => edit(p) }, 'Edit'),
          h('button', { type: 'button', class: 'button button--small button--danger', onclick: async () => {
            if (await confirmDelete(p.name)) {
              try {
                await adminApi.deleteBlockedPeriod(p.id, p.version);
                refresh();
              } catch (e) {
                showError(body, e);
              }
            }
          } }, 'Delete')))))),
    h('button', { type: 'button', class: 'button', onclick: () => edit(null) }, '+ New blocked period'));
}

// ---- Add-ons -----------------------------------------------------------------------------------------------------

async function addons(body: HTMLElement): Promise<void> {
  loading(body);
  let list: Addon[];
  try {
    list = await adminApi.addons();
  } catch (e) {
    clear(body);
    showError(body, e);
    return;
  }
  clear(body);
  const refresh = () => void addons(body);
  const edit = (addon: Addon | null) => {
    const modal = openModal(addon ? `Edit ${addon.name}` : 'New add-on');
    const form = h('div', {},
      field({ label: 'Name', name: 'name', required: true, value: addon?.name }),
      field({ label: 'Description', name: 'description', textarea: true, value: addon?.description }),
      h('label', { class: 'toggle' }, h('input', { type: 'checkbox', name: 'priceOnRequest', checked: addon?.priceOnRequest ?? true }),
        ' Price on request (staff set the price on each booking)'),
      h('div', { class: 'form-grid', 'data-field': 'prices' },
        field({ label: 'Mid-week price', name: 'midweek', type: 'number', value: addon?.prices?.midweek, attrs: { min: 0, step: '0.01' } }),
        field({ label: 'Weekend price', name: 'weekend', type: 'number', value: addon?.prices?.weekend, attrs: { min: 0, step: '0.01' } }),
        field({ label: 'Week price', name: 'week', type: 'number', value: addon?.prices?.week, attrs: { min: 0, step: '0.01' } })),
      h('label', { class: 'toggle' }, h('input', { type: 'checkbox', name: 'quantityApplies', checked: addon?.quantityApplies ?? false }),
        ' Quantity applies (e.g. bags of ice)'),
      h('label', { class: 'toggle' }, h('input', { type: 'checkbox', name: 'isActive', checked: addon?.isActive ?? true }), ' Active'));
    modal.body.append(form);
    modal.footer.append(h('button', { type: 'button', class: 'button', onclick: () => modal.close() }, 'Cancel'),
      h('button', { type: 'button', class: 'button button--primary', onclick: async () => {
        const checkedBox = (name: string) => form.querySelector<HTMLInputElement>(`[name="${name}"]`)!.checked;
        const priceOnRequest = checkedBox('priceOnRequest');
        const midweek = numberOrNull(value(form, 'midweek'));
        const weekend = numberOrNull(value(form, 'weekend'));
        const week = numberOrNull(value(form, 'week'));
        const request = {
          version: addon?.version ?? 0,
          name: value(form, 'name'),
          description: value(form, 'description') || null,
          priceOnRequest,
          prices: priceOnRequest || midweek === null || weekend === null || week === null ? null : { midweek, weekend, week },
          quantityApplies: checkedBox('quantityApplies'),
          isActive: checkedBox('isActive'),
        };
        try {
          if (addon) await adminApi.updateAddon(addon.id, request);
          else await adminApi.createAddon(request);
          modal.close();
          toast('Add-on saved', 'success');
          refresh();
        } catch (e) {
          showError(form, e);
        }
      } }, 'Save'));
  };
  body.append(
    h('table', { class: 'table' },
      h('thead', {}, h('tr', {}, h('th', {}, 'Add-on'), h('th', {}, 'Mid-week / Weekend / Week'), h('th', {}, 'Quantity'), h('th', {}, 'Status'), h('th', {}, h('span', { class: 'visually-hidden' }, 'Actions')))),
      h('tbody', {}, ...list.map((a) => h('tr', { class: a.isActive ? '' : 'row--muted' },
        h('td', {}, a.name),
        h('td', {}, a.priceOnRequest || !a.prices ? 'Price on request' : `${formatMoney(a.prices.midweek)} / ${formatMoney(a.prices.weekend)} / ${formatMoney(a.prices.week)}`),
        h('td', {}, a.quantityApplies ? 'Yes' : 'No'),
        h('td', {}, a.isActive ? 'Active' : 'Inactive'),
        h('td', { class: 'nowrap' },
          h('button', { type: 'button', class: 'button button--small', onclick: () => edit(a) }, 'Edit'),
          a.isActive ? h('button', { type: 'button', class: 'button button--small button--danger', onclick: async () => {
            try {
              await adminApi.deactivateAddon(a.id, a.version);
              refresh();
            } catch (e) {
              showError(body, e);
            }
          } }, 'Deactivate') : ''))))),
    h('button', { type: 'button', class: 'button', onclick: () => edit(null) }, '+ New add-on'));
}

// ---- Email templates ---------------------------------------------------------------------------------------------

async function templates(body: HTMLElement): Promise<void> {
  loading(body);
  try {
    const data = await adminApi.templates();
    clear(body);
    const select = h('select', { 'aria-label': 'Template', class: 'input-wide' },
      ...data.templates.map((t) => h('option', { value: t.key }, TEMPLATE_LABELS[t.key] ?? t.key)));
    const editor = h('div', {});
    const show = () => {
      const t = data.templates.find((x) => x.key === select.value)!;
      clear(editor);
      const form = h('div', {},
        field({ label: 'Subject', name: 'subject', required: true, value: t.subject }),
        field({ label: 'HTML body', name: 'htmlBody', textarea: true, required: true, value: t.htmlBody, attrs: { rows: 18, class: 'code' } }));
      editor.append(form,
        h('div', { class: 'inline-form' },
          h('button', { type: 'button', class: 'button button--primary', onclick: async () => {
            clearErrors(form);
            try {
              const saved = await adminApi.updateTemplate(t.key, { version: t.version, subject: value(form, 'subject'), htmlBody: value(form, 'htmlBody') });
              Object.assign(t, saved);
              toast('Template saved', 'success');
            } catch (e) {
              showError(form, e);
            }
          } }, 'Save template'),
          h('button', { type: 'button', class: 'button', onclick: async () => {
            try {
              await adminApi.testTemplate(t.key as EmailTemplateKey);
              toast('Test email sent to the staff alert address', 'success');
            } catch (e) {
              showError(form, e);
            }
          } }, 'Send test')));
    };
    select.addEventListener('change', show);
    body.append(
      h('p', { class: 'field__hint' }, 'Cancellations and add-on confirmations are not emailed by the system; staff contact the customer.'),
      h('div', { class: 'field' }, h('label', {}, 'Template'), select),
      h('div', { class: 'templates' }, editor,
        h('aside', { class: 'placeholders', 'aria-label': 'Placeholders' }, h('h3', {}, 'Placeholders'),
          h('ul', { class: 'plain-list' }, ...data.placeholders.map((p) => h('li', {}, h('code', {}, `{{${p}}}`)))))));
    show();
  } catch (e) {
    clear(body);
    showError(body, e);
  }
}

async function confirmDelete(what: string): Promise<boolean> {
  return (await openDialog({ title: 'Delete?', body: `Delete ${what}?`, actions: [{ label: 'Cancel', value: 'no' }, { label: 'Delete', value: 'yes', primary: true }] })) === 'yes';
}
