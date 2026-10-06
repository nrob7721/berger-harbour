const money = new Intl.NumberFormat('en-AU', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/** $1,234.00 */
export function formatMoney(amount: number | null | undefined): string {
  if (amount === null || amount === undefined) return '';
  return (amount < 0 ? '-$' : '$') + money.format(Math.abs(amount));
}

export function escapeHtml(value: unknown): string {
  return String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}
