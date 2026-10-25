import { AccountInput, CardInput, Currency } from '../core/api.models';
export type AccountDraftValues = Pick<AccountInput, 'name' | 'bankName' | 'currency' | 'openingBalance' | 'openingDate'>;

export function accountDraft(values: AccountDraftValues, id = crypto.randomUUID()): AccountInput { return { ...values, id }; }
export function cardDebtIsValid(card: CardInput): boolean { return validAmount(card.statementDebtArs, true) && validAmount(card.statementDebtUsd, true); }
export function validAmount(raw: string, nonnegative = false): boolean {
  if (raw === '') return true;
  if (!/^-?\d+(?:\.\d{0,2})?$/.test(raw)) return false;
  const amount = toMinorUnits(raw);
  return amount !== null && (!nonnegative || amount >= 0n) && amount >= -999999999999999999n && amount <= 999999999999999999n;
}
export function toMinorUnits(raw: string): bigint | null {
  if (!/^-?\d+(?:\.\d{0,2})?$/.test(raw)) return null;
  const negative = raw.startsWith('-');
  const [whole, fraction = ''] = (negative ? raw.slice(1) : raw).split('.');
  const cents = BigInt(whole) * 100n + BigInt((fraction + '00').slice(0, 2));
  return negative ? -cents : cents;
}
export function openingTotals(accounts: AccountInput[]): Record<Currency, string> {
  const totals: Record<Currency, bigint> = { ARS: 0n, USD: 0n };
  for (const account of accounts) { const amount = toMinorUnits(account.openingBalance); if (amount !== null) totals[account.currency] += amount; }
  return { ARS: formatMinorUnits(totals.ARS), USD: formatMinorUnits(totals.USD) };
}
export function formatMinorUnits(value: bigint): string {
  const sign = value < 0n ? '-' : ''; const absolute = value < 0n ? -value : value;
  return `${sign}${absolute / 100n}.${String(absolute % 100n).padStart(2, '0')}`;
}
export function formatMoney(raw: string, currency: Currency): string {
  const amount = toMinorUnits(raw); if (amount === null) return raw || '—';
  const negative = amount < 0n;
  const [whole, cents] = formatMinorUnits(negative ? -amount : amount).split('.');
  const grouped = new Intl.NumberFormat('es-AR', { maximumFractionDigits: 0 }).format(BigInt(whole));
  return `${currency === 'ARS' ? '$' : 'US$'} ${negative ? '-' : ''}${grouped},${cents}`;
}
export function createAccountDraft(): AccountInput { return accountDraft({ name: '', bankName: '', currency: 'ARS', openingBalance: '', openingDate: dateToday() }); }
export function createCardDraft(): CardInput { return { id: crypto.randomUUID(), name: '', bankName: '', closingDay: null, dueDay: null, statementDebtArs: '', statementDebtUsd: '', asOfDate: dateToday() }; }
function dateToday(): string { const date = new Date(); return `${date.getFullYear()}-${String(date.getMonth()+1).padStart(2,'0')}-${String(date.getDate()).padStart(2,'0')}`; }

export function normalizeDecimal(raw: string): string {
  const trimmed = raw.trim();
  if (trimmed.includes(',') && !trimmed.includes('.') && /^-?\d+,\d{1,2}$/.test(trimmed)) return trimmed.replace(',', '.');
  return trimmed;
}
