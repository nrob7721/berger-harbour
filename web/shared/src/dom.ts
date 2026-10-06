/** Small DOM helpers; the apps use no UI framework. */

export function $<T extends HTMLElement = HTMLElement>(selector: string, root: ParentNode = document): T {
  const el = root.querySelector<T>(selector);
  if (!el) throw new Error(`Missing element ${selector}`);
  return el;
}

export function $$<T extends HTMLElement = HTMLElement>(selector: string, root: ParentNode = document): T[] {
  return Array.from(root.querySelectorAll<T>(selector));
}

type Attrs = Record<string, string | number | boolean | null | undefined | ((e: Event) => void)>;

/** Creates an element. Children that are strings become text nodes (never HTML). */
export function h<K extends keyof HTMLElementTagNameMap>(tag: K, attrs: Attrs = {}, ...children: (Node | string | null | undefined | false)[]): HTMLElementTagNameMap[K] {
  const el = document.createElement(tag);
  for (const [key, value] of Object.entries(attrs)) {
    if (value === null || value === undefined || value === false) continue;
    if (typeof value === 'function') {
      el.addEventListener(key.replace(/^on/, '').toLowerCase(), value as EventListener);
    } else if (key === 'class') {
      el.className = String(value);
    } else if (value === true) {
      el.setAttribute(key, '');
    } else {
      el.setAttribute(key, String(value));
    }
  }
  for (const child of children) {
    if (child === null || child === undefined || child === false) continue;
    el.append(typeof child === 'string' ? document.createTextNode(child) : child);
  }
  return el;
}

export function clear(el: Element): void {
  while (el.firstChild) el.removeChild(el.firstChild);
}

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

export interface DialogOptions {
  title: string;
  body: Node | string;
  /** Buttons from left to right; the last one is the primary action. */
  actions: { label: string; value: string; primary?: boolean }[];
  /** Value returned when the dialog is dismissed with Esc or the close button. */
  dismissValue?: string;
  wide?: boolean;
}

/**
 * Accessible modal dialog: focus moves into the dialog and is trapped, Esc closes, focus returns to the
 * previously focused element. Resolves with the chosen action's value.
 */
export function openDialog(options: DialogOptions): Promise<string> {
  return new Promise((resolve) => {
    const previouslyFocused = document.activeElement as HTMLElement | null;
    const titleId = `dlg-${Math.random().toString(36).slice(2)}`;
    const close = (value: string) => {
      overlay.remove();
      document.removeEventListener('keydown', onKey, true);
      document.body.classList.remove('has-dialog');
      previouslyFocused?.focus();
      resolve(value);
    };
    const dismiss = options.dismissValue ?? options.actions[0]?.value ?? 'close';
    const dialog = h('div', { class: `dialog${options.wide ? ' dialog--wide' : ''}`, role: 'dialog', 'aria-modal': 'true', 'aria-labelledby': titleId },
      h('div', { class: 'dialog__header' },
        h('h2', { id: titleId, class: 'dialog__title' }, options.title),
        h('button', { type: 'button', class: 'icon-button', 'aria-label': 'Close', onclick: () => close(dismiss) }, '×'),
      ),
      h('div', { class: 'dialog__body' }, typeof options.body === 'string' ? h('p', {}, options.body) : options.body),
      h('div', { class: 'dialog__actions' },
        ...options.actions.map((a) =>
          h('button', { type: 'button', class: a.primary ? 'button button--primary' : 'button', onclick: () => close(a.value) }, a.label)),
      ),
    );
    const overlay = h('div', { class: 'dialog-overlay' }, dialog);
    overlay.addEventListener('mousedown', (e) => {
      if (e.target === overlay) close(dismiss);
    });

    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        e.preventDefault();
        close(dismiss);
      } else if (e.key === 'Tab') {
        const focusable = Array.from(dialog.querySelectorAll<HTMLElement>(FOCUSABLE));
        if (focusable.length === 0) return;
        const first = focusable[0]!;
        const last = focusable[focusable.length - 1]!;
        if (e.shiftKey && document.activeElement === first) {
          e.preventDefault();
          last.focus();
        } else if (!e.shiftKey && document.activeElement === last) {
          e.preventDefault();
          first.focus();
        }
      }
    }

    document.addEventListener('keydown', onKey, true);
    document.body.append(overlay);
    document.body.classList.add('has-dialog');
    const primary = dialog.querySelector<HTMLElement>('.button--primary') ?? dialog.querySelector<HTMLElement>(FOCUSABLE);
    primary?.focus();
  });
}

/** Shows a short status message that screen readers announce. */
export function toast(message: string, kind: 'info' | 'success' | 'error' = 'info'): void {
  let region = document.getElementById('toast-region');
  if (!region) {
    region = h('div', { id: 'toast-region', class: 'toast-region', role: 'status', 'aria-live': 'polite' });
    document.body.append(region);
  }
  const item = h('div', { class: `toast toast--${kind}` }, message);
  region.append(item);
  setTimeout(() => item.remove(), 5000);
}
