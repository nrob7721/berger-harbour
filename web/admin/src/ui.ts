import { clear, h } from '@shared/dom';
import { ApiError } from '@shared/http';

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

export interface Modal {
  body: HTMLElement;
  footer: HTMLElement;
  setTitle(title: string): void;
  close(): void;
}

/** A modal shell for larger forms: focus trap, Esc to close, focus restored on close. */
export function openModal(title: string, options: { wide?: boolean; onClose?: () => void } = {}): Modal {
  const previouslyFocused = document.activeElement as HTMLElement | null;
  const titleId = `modal-${Math.random().toString(36).slice(2)}`;
  const heading = h('h2', { id: titleId, class: 'dialog__title' }, title);
  const body = h('div', { class: 'dialog__body' });
  const footer = h('div', { class: 'dialog__actions' });
  const dialog = h('div', { class: `dialog${options.wide ? ' dialog--wide' : ''}`, role: 'dialog', 'aria-modal': 'true', 'aria-labelledby': titleId },
    h('div', { class: 'dialog__header' }, heading,
      h('button', { type: 'button', class: 'icon-button', 'aria-label': 'Close', onclick: () => close() }, '×')),
    body, footer);
  const overlay = h('div', { class: 'dialog-overlay' }, dialog);

  function onKey(e: KeyboardEvent) {
    if (document.querySelectorAll('.dialog-overlay').length > 1 && overlay !== document.querySelector('.dialog-overlay:last-of-type')) return;
    if (e.key === 'Escape') {
      e.preventDefault();
      close();
    } else if (e.key === 'Tab') {
      const items = Array.from(dialog.querySelectorAll<HTMLElement>(FOCUSABLE)).filter((el) => el.offsetParent !== null);
      if (items.length === 0) return;
      const first = items[0]!;
      const last = items[items.length - 1]!;
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault();
        first.focus();
      }
    }
  }

  function close() {
    overlay.remove();
    document.removeEventListener('keydown', onKey, true);
    if (!document.querySelector('.dialog-overlay')) document.body.classList.remove('has-dialog');
    previouslyFocused?.focus();
    options.onClose?.();
  }

  document.addEventListener('keydown', onKey, true);
  document.body.append(overlay);
  document.body.classList.add('has-dialog');
  setTimeout(() => dialog.querySelector<HTMLElement>(`.dialog__body ${FOCUSABLE}`)?.focus(), 0);
  return { body, footer, setTitle: (t) => (heading.textContent = t), close };
}

export interface FieldOptions {
  label: string;
  name: string;
  type?: string;
  value?: string | number | null;
  required?: boolean;
  hint?: string;
  options?: { value: string; label: string }[];
  attrs?: Record<string, string | number | boolean>;
  textarea?: boolean;
}

/** A labelled form field. The control's name is the API field name, so server errors can be shown next to it. */
export function field(o: FieldOptions): HTMLElement {
  const id = `f-${o.name}-${Math.random().toString(36).slice(2, 7)}`;
  let control: HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement;
  if (o.options) {
    control = h('select', { id, name: o.name, required: o.required },
      ...o.options.map((opt) => h('option', { value: opt.value, selected: String(o.value ?? '') === opt.value }, opt.label)));
  } else if (o.textarea) {
    control = h('textarea', { id, name: o.name, required: o.required });
    control.value = o.value === null || o.value === undefined ? '' : String(o.value);
  } else {
    control = h('input', { id, name: o.name, type: o.type ?? 'text', required: o.required });
    control.value = o.value === null || o.value === undefined ? '' : String(o.value);
  }
  for (const [k, v] of Object.entries(o.attrs ?? {})) {
    if (v === true) control.setAttribute(k, '');
    else if (v !== false) control.setAttribute(k, String(v));
  }
  return h('div', { class: 'field', 'data-field': o.name },
    h('label', { for: id }, o.label + (o.required ? ' *' : '')),
    control,
    o.hint ? h('span', { class: 'field__hint' }, o.hint) : null);
}

export function value(root: ParentNode, name: string): string {
  const el = root.querySelector<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(`[name="${name}"]`);
  return el?.value.trim() ?? '';
}

export function checked(root: ParentNode, name: string): boolean {
  return root.querySelector<HTMLInputElement>(`[name="${name}"]`)?.checked ?? false;
}

export function numberOrNull(text: string): number | null {
  if (text === '') return null;
  const n = Number(text);
  return Number.isFinite(n) ? n : null;
}

export function clearErrors(root: ParentNode): void {
  root.querySelectorAll('.field__error').forEach((e) => e.remove());
  root.querySelectorAll('.field--invalid').forEach((e) => e.classList.remove('field--invalid'));
  root.querySelectorAll('.form-error').forEach((e) => e.remove());
}

/**
 * Shows an API error: field errors next to their fields, conflicts and other messages at the top of the form.
 * Field names are matched case-insensitively against the controls' names.
 */
export function showError(root: HTMLElement, error: unknown, onReload?: () => void): void {
  clearErrors(root);
  const banner = h('div', { class: 'notice notice--error form-error', role: 'alert' });
  if (error instanceof ApiError) {
    const unplaced: string[] = [];
    for (const [name, messages] of Object.entries(error.fieldErrors)) {
      const wrapper = root.querySelector<HTMLElement>(`[data-field="${name}" i]`);
      if (wrapper) {
        wrapper.classList.add('field--invalid');
        wrapper.append(h('span', { class: 'field__error' }, messages.join(' ')));
      } else {
        unplaced.push(...messages);
      }
    }
    if (error.isVersionConflict) {
      banner.append(h('span', {}, error.title + ' '));
      if (onReload) banner.append(h('button', { type: 'button', class: 'button button--small', onclick: onReload }, 'Reload'));
    } else if (error.isConflict) {
      banner.append(error.title);
      if (error.conflictingReferences.length > 0) {
        banner.append(h('div', {}, 'Conflicts with: ', h('strong', {}, error.conflictingReferences.join(', '))));
      }
    } else if (unplaced.length > 0 || Object.keys(error.fieldErrors).length === 0) {
      banner.append(unplaced.length > 0 ? unplaced.join(' ') : error.title);
    } else {
      banner.append('Please correct the highlighted fields.');
    }
  } else {
    banner.append((error as Error)?.message ?? 'Something went wrong.');
  }
  root.prepend(banner);
  banner.scrollIntoView({ block: 'nearest' });
}

export function loading(container: HTMLElement, text = 'Loading…'): void {
  clear(container);
  container.append(h('p', { class: 'muted', 'aria-busy': 'true' }, text));
}

export function tabs(items: { id: string; label: string; render: (panel: HTMLElement) => void }[]): HTMLElement {
  const list = h('div', { class: 'tabs', role: 'tablist' });
  const panel = h('div', { class: 'tab-panel', role: 'tabpanel', tabindex: 0 });
  const buttons = items.map((item, index) => {
    const button = h('button', { type: 'button', role: 'tab', class: 'tab', id: `tab-${item.id}`, 'aria-selected': index === 0 ? 'true' : 'false',
      tabindex: index === 0 ? 0 : -1 }, item.label);
    button.addEventListener('click', () => select(index));
    button.addEventListener('keydown', (e) => {
      if (e.key === 'ArrowRight' || e.key === 'ArrowLeft') {
        const next = (index + (e.key === 'ArrowRight' ? 1 : items.length - 1)) % items.length;
        select(next);
        buttons[next]!.focus();
      }
    });
    list.append(button);
    return button;
  });
  function select(index: number) {
    buttons.forEach((b, i) => {
      b.setAttribute('aria-selected', i === index ? 'true' : 'false');
      b.tabIndex = i === index ? 0 : -1;
    });
    panel.setAttribute('aria-labelledby', buttons[index]!.id);
    clear(panel);
    items[index]!.render(panel);
  }
  select(0);
  return h('div', { class: 'tabs-wrapper' }, list, panel);
}

export const STATUS_LABEL: Record<string, string> = {
  PendingPayment: 'Pending payment',
  Active: 'Active',
  Superseded: 'Superseded',
  Cancelled: 'Cancelled',
  Expired: 'Expired',
};

export const PAYMENT_STATUS_LABEL: Record<string, string> = {
  Outstanding: 'Outstanding',
  DepositPaid: 'Deposit paid',
  PartPaid: 'Part paid',
  FullyPaid: 'Fully paid',
};

export const PERIOD_LABEL: Record<string, string> = {
  Midweek: 'Mid-week',
  Weekend: 'Weekend',
  Week: 'Week',
  LongWeekend: 'Long weekend',
  Custom: 'Custom',
};
