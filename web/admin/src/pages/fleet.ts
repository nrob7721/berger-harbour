import { formatDate } from '@shared/dates';
import { clear, h, openDialog, toast } from '@shared/dom';
import { formatMoney } from '@shared/format';
import { adminApi, type Addon, type BoatDetail, type BoatRate, type BoatUpsert, type PeriodType, type Season, type Unavailability } from '../api';
import { clearErrors, field, loading, numberOrNull, openModal, PERIOD_LABEL, showError, tabs, value } from '../ui';

const RATE_PERIODS: PeriodType[] = ['Midweek', 'Weekend', 'Week', 'LongWeekend'];

export async function renderFleet(page: HTMLElement): Promise<void> {
  const grid = h('div', { class: 'card-grid' });
  const reload = async () => {
    loading(grid);
    try {
      const boats = await adminApi.boats();
      clear(grid);
      for (const boat of boats) {
        grid.append(h('button', { type: 'button', class: 'boat-card', onclick: () => void openBoatDialog(boat.id, reload) },
          boatImage(boat.imageUrl, 'boat-card__image'),
          h('span', { class: 'boat-card__name' }, boat.name),
          h('span', { class: `badge ${boat.isActive ? 'badge--active' : 'badge--inactive'}` }, boat.isActive ? 'Active' : 'Inactive'),
          h('span', { class: 'boat-card__meta' },
            `Max ${boat.maxNoOfGuests} guests · ${boat.adultBeds ?? '?'} adult + ${boat.kidsBeds ?? '?'} kids beds`),
          boat.hasSpa || boat.isPetFriendly
            ? h('span', { class: 'boat-card__features' },
              boat.hasSpa ? h('span', { class: 'badge badge--feature' }, 'Spa') : null,
              boat.isPetFriendly ? h('span', { class: 'badge badge--feature' }, 'Pet friendly') : null)
            : null,
          h('span', { class: 'boat-card__slug' }, boat.slug)));
      }
    } catch (e) {
      clear(grid);
      showError(grid, e);
    }
  };
  page.append(
    h('div', { class: 'toolbar' }, h('h1', { class: 'toolbar__title' }, 'Fleet'),
      h('button', { type: 'button', class: 'button button--primary', onclick: () => void openBoatDialog(null, reload) }, '+ New boat')),
    grid);
  await reload();
}

/** The boat's image, or a neutral placeholder when it is empty or fails to load. */
function boatImage(url: string | null, className: string): HTMLElement {
  const placeholder = () => h('span', { class: `${className} image-placeholder`, 'aria-hidden': 'true' });
  if (!url) return placeholder();
  const img = h('img', { class: className, src: url, alt: '', loading: 'lazy', decoding: 'async' });
  img.addEventListener('error', () => img.replaceWith(placeholder()), { once: true });
  return img;
}

async function openBoatDialog(id: string | null, onSaved: () => Promise<void>): Promise<void> {
  const modal = openModal(id ? 'Boat' : 'New boat', { wide: true });
  loading(modal.body);
  let boat: BoatDetail | null;
  let seasons: Season[];
  let addons: Addon[];
  let defaultDeposit: number;
  try {
    let business;
    [boat, seasons, addons, business] = await Promise.all([id ? adminApi.boat(id) : Promise.resolve(null), adminApi.seasons(),
      adminApi.addons(), adminApi.business()]);
    defaultDeposit = business.depositAmount;
  } catch (e) {
    clear(modal.body);
    showError(modal.body, e);
    return;
  }

  // Draft state shared by the tabs; saved together with "Save boat".
  const draft = {
    name: boat?.name ?? '',
    slug: boat?.slug ?? '',
    maxNoOfGuests: boat?.maxNoOfGuests ?? 8,
    adultBeds: boat?.adultBeds ?? null as number | null,
    kidsBeds: boat?.kidsBeds ?? null as number | null,
    beddingDescription: boat?.beddingDescription ?? '',
    securityBond: boat?.securityBond ?? 0,
    depositAmount: boat?.depositAmount ?? null as number | null,
    imageUrl: boat?.imageUrl ?? '',
    hasSpa: boat?.hasSpa ?? false,
    isPetFriendly: boat?.isPetFriendly ?? false,
    isActive: boat?.isActive ?? false,
    rates: new Map<string, number>((boat?.rates ?? []).map((r) => [`${r.seasonId}|${r.periodType}`, r.price])),
    allowedAddonIds: new Set(boat?.allowedAddonIds ?? []),
  };

  const render = () => {
    clear(modal.body);
    clear(modal.footer);
    modal.setTitle(boat ? boat.name : 'New boat');
    if (boat && boat.missingActivationData.length > 0) {
      modal.body.append(h('div', { class: 'notice notice--warning' },
        h('strong', {}, 'Needed before this boat can be activated: '), boat.missingActivationData.join(', '), '.'));
    }
    modal.body.append(tabs([
      { id: 'details', label: 'Details', render: detailsTab },
      { id: 'rates', label: 'Rates', render: ratesTab },
      { id: 'addons', label: 'Add-ons', render: addonsTab },
      { id: 'unavailabilities', label: 'Unavailabilities', render: unavailabilitiesTab },
    ]));
    modal.footer.append(
      h('button', { type: 'button', class: 'button', onclick: () => modal.close() }, 'Close'),
      h('button', { type: 'button', class: 'button button--primary', onclick: () => void save() }, 'Save boat'));
  };

  const bind = (panel: HTMLElement, name: string, apply: (v: string) => void) =>
    panel.querySelector(`[name="${name}"]`)?.addEventListener('input', () => apply(value(panel, name)));

  const detailsTab = (panel: HTMLElement) => {
    const imagePreview = h('div', { class: 'image-preview' });
    const showPreview = () => {
      clear(imagePreview);
      imagePreview.append(boatImage(draft.imageUrl.trim() || null, 'image-preview__image'));
    };
    showPreview();
    panel.append(h('div', { class: 'form-grid' },
      field({ label: 'Name', name: 'name', required: true, value: draft.name }),
      field({ label: 'Slug', name: 'slug', required: true, value: draft.slug, hint: 'Must match the WordPress page URL, e.g. pacific-blue' }),
      field({ label: 'Max guests', name: 'maxNoOfGuests', type: 'number', required: true, value: draft.maxNoOfGuests, attrs: { min: 1 } }),
      field({ label: 'Number of adult beds', name: 'adultBeds', type: 'number', value: draft.adultBeds, attrs: { min: 1 }, hint: 'A double or queen counts as one bed.' }),
      field({ label: 'Number of kids beds', name: 'kidsBeds', type: 'number', value: draft.kidsBeds, attrs: { min: 0 }, hint: 'e.g. bunks; enter 0 if none.' }),
      field({ label: 'Security bond (AUD)', name: 'securityBond', type: 'number', value: draft.securityBond, attrs: { min: 0, step: '0.01' } }),
      field({ label: 'Deposit (AUD)', name: 'depositAmount', type: 'number', value: draft.depositAmount, attrs: { min: 1, step: '0.01' },
        hint: `Leave empty to use the default deposit of ${formatMoney(defaultDeposit)} from Settings.` }),
    ),
    field({ label: 'Bedding description', name: 'beddingDescription', textarea: true, value: draft.beddingDescription,
      hint: 'Shown to customers in the rooming warning, e.g. "4 queen bedrooms, bunk area (2 singles)…"' }),
    field({ label: 'Image URL', name: 'imageUrl', type: 'url', value: draft.imageUrl,
      hint: 'Link to an image on bergerhouseboats.com.au' }),
    imagePreview,
    h('div', { class: 'toggle-row' },
      h('label', { class: 'toggle', 'data-field': 'hasSpa' },
        h('input', { type: 'checkbox', name: 'hasSpa', checked: draft.hasSpa, onchange: (e: Event) => (draft.hasSpa = (e.target as HTMLInputElement).checked) }),
        ' Spa'),
      h('label', { class: 'toggle', 'data-field': 'isPetFriendly' },
        h('input', { type: 'checkbox', name: 'isPetFriendly', checked: draft.isPetFriendly, onchange: (e: Event) => (draft.isPetFriendly = (e.target as HTMLInputElement).checked) }),
        ' Pet friendly')),
    h('label', { class: 'toggle', 'data-field': 'isActive' },
      h('input', { type: 'checkbox', name: 'isActive', checked: draft.isActive, onchange: (e: Event) => (draft.isActive = (e.target as HTMLInputElement).checked) }),
      ' Active (bookable online and by staff)'));
    bind(panel, 'name', (v) => (draft.name = v));
    bind(panel, 'slug', (v) => (draft.slug = v));
    bind(panel, 'maxNoOfGuests', (v) => (draft.maxNoOfGuests = Number(v) || 0));
    bind(panel, 'adultBeds', (v) => (draft.adultBeds = numberOrNull(v)));
    bind(panel, 'kidsBeds', (v) => (draft.kidsBeds = numberOrNull(v)));
    bind(panel, 'securityBond', (v) => (draft.securityBond = Number(v) || 0));
    bind(panel, 'depositAmount', (v) => (draft.depositAmount = numberOrNull(v)));
    bind(panel, 'beddingDescription', (v) => (draft.beddingDescription = v));
    panel.querySelector('[name="imageUrl"]')?.addEventListener('change', () => {
      draft.imageUrl = value(panel, 'imageUrl');
      showPreview();
    });
    bind(panel, 'imageUrl', (v) => (draft.imageUrl = v));
  };

  const ratesTab = (panel: HTMLElement) => {
    const table = h('table', { class: 'table rates-table' },
      h('thead', {}, h('tr', {}, h('th', { scope: 'col' }, 'Season'), ...RATE_PERIODS.map((p) => h('th', { scope: 'col' }, PERIOD_LABEL[p]!)))),
      h('tbody', {}, ...seasons.map((s) => h('tr', {},
        h('th', { scope: 'row' }, s.name),
        ...RATE_PERIODS.map((p) => {
          const key = `${s.id}|${p}`;
          const input = h('input', { type: 'number', min: 0, step: '0.01', class: 'input-sm', value: draft.rates.get(key) ?? '',
            'aria-label': `${s.name} ${PERIOD_LABEL[p]} rate` });
          input.addEventListener('input', () => {
            const n = numberOrNull(input.value);
            if (n === null) draft.rates.delete(key);
            else draft.rates.set(key, n);
          });
          return h('td', {}, input);
        })))));
    panel.append(h('p', { class: 'field__hint' }, 'Prices in AUD including GST. Mid-week, Weekend and Week rates are required for every season before activation; Long weekend is for staff bookings.'), table);
  };

  const addonsTab = (panel: HTMLElement) => {
    panel.append(h('p', { class: 'field__hint' }, 'Add-ons customers can request for this boat (in the order set in Settings).'),
      h('ul', { class: 'check-list' }, ...addons.map((a) => h('li', {}, h('label', { class: 'toggle' },
        h('input', { type: 'checkbox', checked: draft.allowedAddonIds.has(a.id), onchange: (e: Event) => {
          if ((e.target as HTMLInputElement).checked) draft.allowedAddonIds.add(a.id);
          else draft.allowedAddonIds.delete(a.id);
        } }),
        ` ${a.name}`, a.isActive ? '' : ' (inactive)',
        h('span', { class: 'muted' }, a.priceOnRequest || !a.prices ? ' · price on request' : ` · ${formatMoney(a.prices.midweek)} / ${formatMoney(a.prices.weekend)} / ${formatMoney(a.prices.week)}`))))));
  };

  const unavailabilitiesTab = (panel: HTMLElement) => {
    if (!boat) {
      panel.append(h('p', { class: 'muted' }, 'Save the boat first to add unavailabilities.'));
      return;
    }
    void renderUnavailabilities(panel, boat.id);
  };

  const save = async () => {
    clearErrors(modal.body);
    const body: BoatUpsert = {
      version: boat?.version ?? 0,
      name: draft.name,
      slug: draft.slug,
      maxNoOfGuests: draft.maxNoOfGuests,
      adultBeds: draft.adultBeds,
      kidsBeds: draft.kidsBeds,
      beddingDescription: draft.beddingDescription || null,
      securityBond: draft.securityBond,
      depositAmount: draft.depositAmount,
      imageUrl: draft.imageUrl.trim() || null,
      hasSpa: draft.hasSpa,
      isPetFriendly: draft.isPetFriendly,
      rates: [...draft.rates].map(([key, price]): BoatRate => {
        const [seasonId, periodType] = key.split('|') as [string, PeriodType];
        return { seasonId, periodType, price };
      }),
      allowedAddonIds: [...draft.allowedAddonIds],
      isActive: draft.isActive,
    };
    try {
      boat = boat ? await adminApi.updateBoat(boat.id, body) : await adminApi.createBoat(body);
      draft.isActive = boat.isActive;
      toast('Boat saved', 'success');
      render();
      await onSaved();
    } catch (e) {
      showError(modal.body, e, async () => {
        boat = await adminApi.boat(boat!.id);
        render();
      });
    }
  };

  render();
}

async function renderUnavailabilities(panel: HTMLElement, boatId: string): Promise<void> {
  loading(panel);
  let items: Unavailability[];
  try {
    items = await adminApi.unavailabilities(boatId);
  } catch (e) {
    clear(panel);
    showError(panel, e);
    return;
  }
  clear(panel);
  const refresh = () => void renderUnavailabilities(panel, boatId);
  const edit = (existing: Unavailability | null) => {
    const modal = openModal(existing ? 'Edit unavailability' : 'New unavailability');
    const form = h('div', {},
      h('div', { class: 'form-grid' },
        field({ label: 'First night', name: 'firstNight', type: 'date', required: true, value: existing?.firstNight }),
        field({ label: 'Last night', name: 'lastNight', type: 'date', required: true, value: existing?.lastNight })),
      field({ label: 'Comments', name: 'comments', textarea: true, value: existing?.comments }));
    modal.body.append(form);
    modal.footer.append(
      h('button', { type: 'button', class: 'button', onclick: () => modal.close() }, 'Cancel'),
      h('button', { type: 'button', class: 'button button--primary', onclick: async () => {
        const body = { version: existing?.version ?? 0, firstNight: value(form, 'firstNight'), lastNight: value(form, 'lastNight'), comments: value(form, 'comments') || null };
        try {
          if (existing) await adminApi.updateUnavailability(boatId, existing.id, body);
          else await adminApi.createUnavailability(boatId, body);
          modal.close();
          toast('Unavailability saved', 'success');
          refresh();
        } catch (e) {
          showError(form, e);
        }
      } }, 'Save'));
  };

  panel.append(
    h('div', { class: 'toolbar toolbar--compact' },
      h('p', { class: 'field__hint' }, 'Nights the boat cannot be hired (maintenance, etc.). These cannot overlap bookings.'),
      h('button', { type: 'button', class: 'button button--primary button--small', onclick: () => edit(null) }, '+ New Unavailability')),
    items.length === 0
      ? h('p', { class: 'muted' }, 'No unavailabilities.')
      : h('div', { class: 'scroll-table' }, h('table', { class: 'table' },
        h('thead', {}, h('tr', {}, h('th', {}, 'First night'), h('th', {}, 'Last night'), h('th', {}, 'Comments'), h('th', {}, h('span', { class: 'visually-hidden' }, 'Actions')))),
        h('tbody', {}, ...items.map((u) => h('tr', {},
          h('td', {}, formatDate(u.firstNight)), h('td', {}, formatDate(u.lastNight)), h('td', {}, u.comments ?? ''),
          h('td', { class: 'nowrap' },
            h('button', { type: 'button', class: 'button button--small', onclick: () => edit(u) }, 'Edit'),
            h('button', { type: 'button', class: 'button button--small button--danger', onclick: async () => {
              const ok = await openDialog({ title: 'Delete unavailability?', body: `${formatDate(u.firstNight)} – ${formatDate(u.lastNight)}`,
                actions: [{ label: 'Cancel', value: 'no' }, { label: 'Delete', value: 'yes', primary: true }] });
              if (ok !== 'yes') return;
              try {
                await adminApi.deleteUnavailability(boatId, u.id, u.version);
                refresh();
              } catch (e) {
                showError(panel, e);
              }
            } }, 'Delete'))))))));
}
