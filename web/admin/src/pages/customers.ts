import { clear, h } from '@shared/dom';
import { adminApi, type Customer } from '../api';
import { loading, showError } from '../ui';

/** Read-only in v1: contact details are updated from the booking dialog. */
export async function renderCustomers(page: HTMLElement): Promise<void> {
  const filter = h('input', { type: 'search', placeholder: 'Filter by name, email or mobile', 'aria-label': 'Filter customers', class: 'search__input' });
  const container = h('div', {});
  const count = h('p', { class: 'muted', 'aria-live': 'polite' });
  page.append(h('div', { class: 'toolbar' }, h('h1', { class: 'toolbar__title' }, 'Customers'), filter), count, container);

  loading(container);
  let customers: Customer[];
  try {
    customers = await adminApi.customers();
  } catch (e) {
    clear(container);
    showError(container, e);
    return;
  }

  const render = () => {
    const term = filter.value.trim().toLowerCase();
    const rows = customers.filter((c) => !term || c.fullName.toLowerCase().includes(term) || c.email.includes(term) || (c.mobileNumber ?? '').includes(term));
    count.textContent = `${rows.length} of ${customers.length} customers`;
    clear(container);
    container.append(h('div', { class: 'scroll-table' }, h('table', { class: 'table' },
      h('thead', {}, h('tr', {}, h('th', {}, 'Name'), h('th', {}, 'Email'), h('th', {}, 'Mobile'), h('th', {}, 'Created'))),
      h('tbody', {}, ...rows.map((c) => h('tr', {},
        h('td', {}, c.fullName),
        h('td', {}, h('a', { href: `mailto:${c.email}` }, c.email)),
        h('td', {}, c.mobileNumber ?? ''),
        h('td', {}, new Date(c.createdDate).toLocaleDateString('en-AU', { timeZone: 'Australia/Sydney', day: '2-digit', month: '2-digit', year: 'numeric' }))))))));
  };
  filter.addEventListener('input', render);
  render();
}
