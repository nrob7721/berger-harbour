import { getCountries, getCountryCallingCode, parsePhoneNumberFromString, type CountryCode } from 'libphonenumber-js';
import { h } from './dom';

interface Country {
  code: CountryCode;
  name: string;
  dial: string;
}

let countryCache: Country[] | null = null;

function countries(): Country[] {
  if (countryCache) return countryCache;
  const names = new Intl.DisplayNames(['en-AU'], { type: 'region' });
  countryCache = getCountries()
    .map(code => ({ code, name: names.of(code) ?? code, dial: `+${getCountryCallingCode(code)}` }))
    .sort((a, b) => (a.code === 'AU' ? -1 : b.code === 'AU' ? 1 : a.name.localeCompare(b.name)));
  return countryCache;
}

function flag(code: string): string {
  return String.fromCodePoint(...[...code.toUpperCase()].map(c => 0x1f1e6 + c.charCodeAt(0) - 65));
}

let instance = 0;

/**
 * Phone number field with a searchable country-code selector (default Australia +61).
 * Validated with libphonenumber-js; the value is E.164.
 */
export class PhoneInput {
  readonly element: HTMLElement;
  readonly input: HTMLInputElement;
  private country: CountryCode = 'AU';
  private readonly button: HTMLButtonElement;
  private readonly popover: HTMLElement;
  private readonly search: HTMLInputElement;
  private readonly list: HTMLUListElement;
  private active = 0;
  private filtered: Country[] = [];

  constructor(private readonly options: { id: string; label: string; required: boolean; mobile?: boolean }) {
    const listId = `phone-countries-${++instance}`;
    this.button = h('button', {
      type: 'button', class: 'phone-country', 'aria-haspopup': 'listbox', 'aria-expanded': 'false',
      'aria-label': `Country code for ${options.label}`, onclick: () => this.toggle(),
    });
    this.search = h('input', {
      type: 'search', class: 'phone-search', placeholder: 'Search countries', 'aria-label': 'Search countries', 'aria-controls': listId,
      autocomplete: 'off', oninput: () => this.renderList(), onkeydown: (e: Event) => this.onSearchKey(e as KeyboardEvent),
    });
    this.list = h('ul', { id: listId, role: 'listbox', class: 'phone-list', 'aria-label': 'Countries' });
    this.popover = h('div', { class: 'phone-popover', hidden: true }, this.search, this.list);
    this.input = h('input', {
      id: options.id, type: 'tel', inputmode: 'tel', autocomplete: options.mobile ? 'tel' : 'off', required: options.required,
      class: 'phone-number', 'aria-describedby': `${options.id}-error`,
    });
    this.element = h('div', { class: 'phone-input' }, this.button, this.input, this.popover);
    document.addEventListener('click', e => {
      if (!this.element.contains(e.target as Node)) this.close();
    });
    this.renderButton();
  }

  /** E.164 value, or null when empty or invalid. */
  get value(): string | null {
    const text = this.input.value.trim();
    if (!text) return null;
    const parsed = parsePhoneNumberFromString(text, this.country);
    return parsed?.isValid() ? parsed.number : null;
  }

  set value(e164: string | null | undefined) {
    if (!e164) {
      this.input.value = '';
      return;
    }
    const parsed = parsePhoneNumberFromString(e164);
    if (parsed?.country) this.country = parsed.country;
    this.input.value = parsed ? parsed.formatNational() : e164;
    this.renderButton();
  }

  /** An error message, or null when valid. */
  validate(): string | null {
    const text = this.input.value.trim();
    if (!text) return this.options.required ? `${this.options.label} is required.` : null;
    return this.value ? null : `Please enter a valid ${this.options.label.toLowerCase()} for the selected country.`;
  }

  set disabled(value: boolean) {
    this.input.disabled = value;
    this.button.disabled = value;
  }

  private renderButton(): void {
    const c = countries().find(x => x.code === this.country)!;
    this.button.textContent = `${flag(c.code)} ${c.dial}`;
    this.button.title = c.name;
  }

  private toggle(): void {
    if (this.popover.hidden) this.open();
    else this.close();
  }

  private open(): void {
    this.popover.hidden = false;
    this.button.setAttribute('aria-expanded', 'true');
    this.search.value = '';
    this.renderList();
    this.search.focus();
  }

  private close(): void {
    if (this.popover.hidden) return;
    this.popover.hidden = true;
    this.button.setAttribute('aria-expanded', 'false');
  }

  private renderList(): void {
    const term = this.search.value.trim().toLowerCase();
    this.filtered = countries().filter(c => !term || c.name.toLowerCase().includes(term) || c.dial.includes(term) || c.code.toLowerCase() === term);
    this.active = 0;
    this.list.replaceChildren(...this.filtered.slice(0, 300).map((c, i) =>
      h('li', {
        role: 'option', id: `${this.list.id}-${c.code}`, 'aria-selected': i === this.active ? 'true' : 'false',
        class: c.code === this.country ? 'current' : '', onclick: () => this.choose(c.code),
      }, `${flag(c.code)} ${c.name} `, h('span', { class: 'dial' }, c.dial))));
    this.syncActive();
  }

  private syncActive(): void {
    [...this.list.children].forEach((li, i) => li.setAttribute('aria-selected', i === this.active ? 'true' : 'false'));
    const current = this.list.children[this.active];
    if (current) {
      this.search.setAttribute('aria-activedescendant', current.id);
      current.scrollIntoView?.({ block: 'nearest' });
    }
  }

  private onSearchKey(e: KeyboardEvent): void {
    if (e.key === 'ArrowDown') this.active = Math.min(this.active + 1, this.filtered.length - 1);
    else if (e.key === 'ArrowUp') this.active = Math.max(this.active - 1, 0);
    else if (e.key === 'Enter') {
      const c = this.filtered[this.active];
      if (c) this.choose(c.code);
    } else if (e.key === 'Escape') {
      this.close();
      this.button.focus();
    } else return;
    e.preventDefault();
    this.syncActive();
  }

  private choose(code: CountryCode): void {
    this.country = code;
    this.renderButton();
    this.close();
    this.input.focus();
  }
}
