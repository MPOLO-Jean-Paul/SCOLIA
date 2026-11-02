const fr = new Intl.NumberFormat('fr-FR', { maximumFractionDigits: 2 });
const frInt = new Intl.NumberFormat('fr-FR', { maximumFractionDigits: 0 });
let currency = 'FC';

export function setCurrency(symbol: string) {
  currency = symbol || 'FC';
}
export function getCurrency() {
  return currency;
}
export const money = (v: number | null | undefined) => (v === null || v === undefined ? '—' : `${frInt.format(v)} ${currency}`);
export const num = (v: number | null | undefined, digits = 2) =>
  v === null || v === undefined ? '—' : new Intl.NumberFormat('fr-FR', { maximumFractionDigits: digits }).format(v);
export const int = (v: number | null | undefined) => (v === null || v === undefined ? '—' : frInt.format(v));
export const pct = (v: number | null | undefined) => (v === null || v === undefined ? '—' : `${fr.format(v)} %`);

export function date(value?: string | null) {
  if (!value) return '—';
  const [y, m, d] = value.slice(0, 10).split('-');
  return `${d}/${m}/${y}`;
}
export function dateTime(value?: string | null) {
  if (!value) return '—';
  return new Date(value).toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' });
}
export function longDate(value: string) {
  return new Date(value + 'T00:00:00').toLocaleDateString('fr-FR', { day: 'numeric', month: 'long', year: 'numeric' });
}
export function today(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}
export function initials(name: string) {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]?.toUpperCase()).join('');
}
export function fullName(s: { lastName: string; middleName: string; firstName: string }) {
  return [s.lastName, s.middleName, s.firstName].filter(Boolean).join(' ');
}
/** Normalisation identique au serveur : majuscules, sans accents. */
export function key(value: string) {
  return value.normalize('NFD').replace(/[̀-ͯ]/g, '').toUpperCase().trim();
}
export function newKey() {
  return crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(16).slice(2)}`;
}
