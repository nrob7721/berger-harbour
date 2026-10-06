import { $, $$, clear } from '@shared/dom';
import { renderBookings } from './pages/bookings';
import { renderCustomers } from './pages/customers';
import { renderFleet } from './pages/fleet';
import { renderSettings } from './pages/settings';

const routes: Record<string, (page: HTMLElement) => void | Promise<void>> = {
  bookings: renderBookings,
  fleet: renderFleet,
  customers: renderCustomers,
  settings: renderSettings,
};

function route(): void {
  const name = location.hash.replace(/^#\/?/, '').split('/')[0] || 'bookings';
  const render = routes[name] ?? renderBookings;
  for (const link of $$('.topbar__nav a')) {
    if (link.dataset.route === name) link.setAttribute('aria-current', 'page');
    else link.removeAttribute('aria-current');
  }
  const page = $('#page');
  clear(page);
  page.className = `page page--${name}`;
  void render(page);
}

window.addEventListener('hashchange', route);
route();
