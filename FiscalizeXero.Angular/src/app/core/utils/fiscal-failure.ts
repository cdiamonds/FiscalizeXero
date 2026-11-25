import { FiscalFailure, FiscalFailureCategory } from '../models/xero.models';

export interface FailureGuide {
  /** Short name of the kind of problem, shown as a chip. */
  label: string;
  /** What to do about it, in one sentence. */
  hint: string;
  /** Where to fix it, when it's fixed on another page. */
  action?: { label: string; link: string; queryParams?: Record<string, string> };
}

/**
 * How to read and fix each kind of failure. Kept in one place so the list pill and the document page agree.
 * With the document's customer id, a buyer problem links straight to that customer's page (and back via returnTo).
 */
export function failureGuide(category: FiscalFailureCategory | string | null | undefined, message?: string | null,
                             customer?: { id?: string | null; returnTo?: string | null }): FailureGuide {
  switch (category) {
    case 'Buyer': {
      // A wrong TIN/VAT number is corrected, never filled in; missing details are typed on the customer here in Fiscalize.
      const invalid = /is not valid|not a real TIN|but no TIN/i.test(message ?? '');
      const rejected = /^Fiscalize platform/i.test(message ?? '');
      const action: FailureGuide['action'] = customer?.id
        ? { label: 'Fix buyer details', link: `/customers/${customer.id}`, queryParams: customer.returnTo ? { returnTo: customer.returnTo } : undefined }
        : { label: 'Review customers', link: '/customers', queryParams: invalid ? { status: 'Invalid' } : rejected ? undefined : { status: 'Attention' } };
      if (invalid) return {
        label: 'TIN / VAT',
        hint: 'Correct the TIN or VAT number on the customer, then fiscalize again. Lenient validation never patches these.',
        action
      };
      // The platform itself rejected a buyer value: say what it rejected rather than "missing".
      if (rejected) return {
        label: 'Customer details',
        hint: 'ZIMRA rejected the buyer details listed above. Correct them on the customer, then fiscalize again.',
        action
      };
      return {
        label: 'Customer details',
        hint: 'Fill in the missing details on the customer (right here in Fiscalize), then fiscalize again. Lenient validation (Settings → Fiscalization) fills placeholders instead.',
        action
      };
    }
    case 'HsCode':
      return { label: 'HS code', hint: 'Give the item an HS code, or set a default HS code, then fiscalize again.', action: { label: 'Open items', link: '/items' } };
    case 'Device':
      return { label: 'Fiscal device', hint: 'The ZIMRA device is missing or has no API key.', action: { label: 'Device setup', link: '/device-setup' } };
    case 'OriginalSale':
      return { label: 'Original invoice', hint: 'ZIMRA credits a specific fiscal receipt. Allocate the credit note to its invoice in Xero, or choose the invoice on the credit note page; that invoice must be fiscalized first.' };
    case 'Platform':
      return { label: 'ZIMRA / platform', hint: 'The Fiscalize platform rejected the document. Correct the details listed and fiscalize again.' };
    case 'Xero':
      return { label: 'Xero', hint: 'Xero could not be read. Check the connection; the next attempt retries automatically.', action: { label: 'Xero connection', link: '/xero-connection' } };
    case 'Network':
      return { label: 'Network', hint: 'A service didn\'t answer in time. This usually clears on its own; try again in a minute.' };
    default:
      return { label: 'Error', hint: 'Fiscalize again once the problem is fixed. If it keeps failing, send this message to support.' };
  }
}

/** "just now", "5 min ago", "3 h ago", "2 days ago", else the date. */
export function relativeTime(iso: string | null | undefined, now: number = Date.now()): string {
  if (!iso) return '';
  const t = Date.parse(iso);
  if (Number.isNaN(t)) return '';
  const s = Math.max(0, Math.round((now - t) / 1000));
  if (s < 45) return 'just now';
  const m = Math.round(s / 60);
  if (m < 60) return `${m} min ago`;
  const h = Math.round(m / 60);
  if (h < 24) return `${h} h ago`;
  const d = Math.round(h / 24);
  if (d < 7) return `${d} day${d === 1 ? '' : 's'} ago`;
  return new Date(t).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' });
}

/** Full local date and time, for a title attribute. */
export function absoluteTime(iso: string | null | undefined): string {
  if (!iso) return '';
  const t = Date.parse(iso);
  return Number.isNaN(t) ? '' : new Date(t).toLocaleString();
}

/** One line for the source and attempt count: "Automatic · 3 attempts". */
export function failureMeta(f: FiscalFailure): string {
  const source = f.source === 'Manual' ? 'Manual attempt' : 'Automatic';
  return f.attempts > 1 ? `${source} · ${f.attempts} attempts` : source;
}

/** Index failures by document id for quick lookups in list templates. */
export function byDocumentId(failures: FiscalFailure[] | null | undefined): Record<string, FiscalFailure> {
  const map: Record<string, FiscalFailure> = {};
  for (const f of failures ?? []) map[f.documentId] = f;
  return map;
}
