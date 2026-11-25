import { XeroSalesDocument } from '../models/xero.models';

/** How Xero's web app words each document status. */
const STATUS_LABELS: Record<string, string> = {
  DRAFT: 'Draft',
  SUBMITTED: 'Awaiting approval',
  AUTHORISED: 'Awaiting payment',
  PAID: 'Paid',
  VOIDED: 'Voided',
  DELETED: 'Deleted'
};

export function statusLabel(status: string | null | undefined): string {
  const s = (status ?? '').toUpperCase();
  return STATUS_LABELS[s] ?? (status || 'Unknown');
}

/** Only approved documents (awaiting payment or paid) are real sales that can be fiscalized. */
export function isApproved(status: string | null | undefined): boolean {
  const s = (status ?? '').toUpperCase();
  return s === 'AUTHORISED' || s === 'PAID';
}

/** Tailwind classes for a status chip. */
export function statusClasses(status: string | null | undefined): string {
  switch ((status ?? '').toUpperCase()) {
    case 'PAID': return 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300';
    case 'AUTHORISED': return 'bg-sky-100 text-sky-700 dark:bg-sky-500/15 dark:text-sky-300';
    case 'VOIDED':
    case 'DELETED': return 'bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300';
    default: return 'bg-slate-100 text-slate-600 dark:bg-white/10 dark:text-slate-300';
  }
}

/** The status choices offered on document lists. '' is the server default: approved documents. */
export const STATUS_FILTERS: { value: string; label: string }[] = [
  { value: '', label: 'Approved' },
  { value: 'AUTHORISED', label: 'Awaiting payment' },
  { value: 'PAID', label: 'Paid' },
  { value: 'DRAFT', label: 'Draft' },
  { value: 'SUBMITTED', label: 'Awaiting approval' },
  { value: 'VOIDED', label: 'Voided' },
  { value: 'all', label: 'All' }
];

/** Why a document in this status cannot be fiscalized yet, in the user's terms; null when it can. */
export function notFiscalisableReason(status: string | null | undefined, kind: 'invoice' | 'credit note'): string | null {
  switch ((status ?? '').toUpperCase()) {
    case 'AUTHORISED':
    case 'PAID': return null;
    case 'DRAFT': return `This ${kind} is still a draft in Xero. Approve it in Xero, then fiscalize it.`;
    case 'SUBMITTED': return `This ${kind} is awaiting approval in Xero. Approve it in Xero, then fiscalize it.`;
    case 'VOIDED': return `This ${kind} was voided in Xero, so there is nothing to fiscalize.`;
    case 'DELETED': return `This ${kind} was deleted in Xero, so there is nothing to fiscalize.`;
    default: return `Only approved ${kind}s can be fiscalized.`;
  }
}

/** Xero's "Amounts are" setting in words. */
export function amountsAreLabel(lineAmountTypes: string | null | undefined): string {
  switch ((lineAmountTypes ?? '').toLowerCase()) {
    case 'inclusive': return 'Tax inclusive';
    case 'exclusive': return 'Tax exclusive';
    case 'notax': return 'No tax';
    default: return '';
  }
}

/** A line's amount including its tax, whatever the document's "Amounts are" setting. */
export function lineGross(doc: Pick<XeroSalesDocument, 'lineAmountTypes'>, line: { lineAmount?: number | null; taxAmount?: number | null }): number {
  const exclusive = (doc.lineAmountTypes ?? '').toLowerCase() === 'exclusive';
  return (line.lineAmount ?? 0) + (exclusive ? (line.taxAmount ?? 0) : 0);
}

/**
 * The API writes UTC times without a zone ("2026-11-18T11:06:40"), which browsers would read as local time.
 * This adds the Z so date pipes show the right local time. Calendar dates (dateString) must not go through it.
 */
export function asUtc(value: string | null | undefined): string | null {
  if (!value) return null;
  return /[zZ]|[+-]\d{2}:?\d{2}$/.test(value) ? value : `${value}Z`;
}

/** The yyyy-MM-dd part of Xero's calendar dates ("2026-11-23T00:00:00"), safe to format without a time-zone shift. */
export function calendarDate(value: string | null | undefined): string | null {
  return value && value.length >= 10 ? value.slice(0, 10) : value || null;
}
