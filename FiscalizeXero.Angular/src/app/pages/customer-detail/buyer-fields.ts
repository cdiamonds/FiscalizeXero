import { BuyerDetailsValues, SaveCustomerTaxDetails } from '../../core/services/xero/tax-details.service';

/** The ten buyer values ZIMRA uses, as typed in the form. */
export interface BuyerDraft {
  registeredName: string; tradeName: string; tin: string; vatNumber: string;
  phone: string; email: string; houseNo: string; street: string; city: string; province: string;
}
export type BuyerKey = keyof BuyerDraft;

export const EMPTY_DRAFT: BuyerDraft = {
  registeredName: '', tradeName: '', tin: '', vatNumber: '', phone: '', email: '', houseNo: '', street: '', city: '', province: ''
};
export const BUYER_KEYS = Object.keys(EMPTY_DRAFT) as BuyerKey[];

/** One editable buyer field, and where its value comes from in Xero. */
export interface BuyerField {
  key: BuyerKey;
  label: string;
  /** Short format hint after the label, e.g. "10 digits". */
  hint?: string;
  /** Where the value is read from on the Xero contact, shown in the info tip. */
  where: string;
  /** Placeholder when Xero has nothing either. */
  empty: string;
  /** ZIMRA needs it whenever the customer has a TIN. */
  required?: boolean;
  mono?: boolean; type?: string; inputmode?: string; maxlength?: number; autocomplete?: string;
  /** Spans both columns on wide screens. */
  wide?: boolean;
}

export interface BuyerSection { key: string; title: string; description: string; icon: string; fields: BuyerField[] }

/** "Contacts › open the contact › Edit" is said once on the page; each tip names the box. */
export const BUYER_SECTIONS: BuyerSection[] = [
  {
    key: 'business', title: 'Business identity', icon: 'M3 21h18M5 21V7l8-4v18M19 21V11l-6-4M9 9h.01M9 13h.01M9 17h.01',
    description: 'Who the buyer is. A TIN makes this a B2B sale; without one the document is fiscalised as a cash sale.',
    fields: [
      { key: 'registeredName', label: 'Registered name', required: true, empty: 'Legal company name', maxlength: 100, autocomplete: 'organization',
        where: 'The contact name in Xero. A different registered name typed here is kept in Fiscalize; the Xero contact name is not changed.' },
      { key: 'tradeName', label: 'Trade name', empty: 'Same as registered name', maxlength: 50,
        where: 'The contact name in Xero. A different trade name typed here is kept in Fiscalize; the Xero contact name is not changed.' },
      { key: 'tin', label: 'TIN', hint: '10 digits', mono: true, inputmode: 'numeric', empty: 'e.g. 2000123456', maxlength: 14,
        where: 'Financial details › Tax ID number on the Xero contact (a 10-digit value in either number box is read as the TIN). Saving here writes it to that box. Leave it empty for cash-sale customers.' },
      { key: 'vatNumber', label: 'VAT number', hint: '9 digits', mono: true, inputmode: 'numeric', empty: 'e.g. 220123456', maxlength: 13,
        where: 'Business information › Company registration number on the Xero contact (a 9-digit value in either number box is read as the VAT number). Saving here writes it to that box.' },
    ]
  },
  {
    key: 'contact', title: 'Contact', icon: 'M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6A19.79 19.79 0 0 1 2.12 4.18 2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92z',
    description: 'How ZIMRA can reach the buyer.',
    fields: [
      { key: 'phone', label: 'Phone', required: true, mono: true, type: 'tel', inputmode: 'tel', empty: 'e.g. 0771234567', maxlength: 30, autocomplete: 'tel',
        where: 'Phone on the Xero contact; if it is empty we use Mobile, then Direct dial. A number typed here is kept in Fiscalize.' },
      { key: 'email', label: 'Email', required: true, type: 'email', inputmode: 'email', empty: 'e.g. accounts@company.co.zw', maxlength: 200, autocomplete: 'email',
        where: 'Email on the Xero contact. An email typed here is kept in Fiscalize.' },
    ]
  },
  {
    key: 'address', title: 'Address', icon: 'M12 22s7-6.2 7-12a7 7 0 1 0-14 0c0 5.8 7 12 7 12zM12 12.5a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5z',
    description: 'The buyer’s physical address in Zimbabwe.',
    fields: [
      { key: 'houseNo', label: 'House / building no.', required: true, empty: 'e.g. 12', maxlength: 50,
        where: 'Billing address on the Xero contact, at the start of the address (e.g. 12 Samora Machel Avenue); the delivery address is used when billing is empty. A value typed here is kept in Fiscalize.' },
      { key: 'street', label: 'Street', required: true, empty: 'e.g. Samora Machel Avenue', maxlength: 100, autocomplete: 'address-line1',
        where: 'Billing address lines on the Xero contact. A value typed here is kept in Fiscalize.' },
      { key: 'city', label: 'City / town', required: true, empty: 'e.g. Harare', maxlength: 50, autocomplete: 'address-level2',
        where: 'Billing address › Town / City on the Xero contact. A value typed here is kept in Fiscalize.' },
      { key: 'province', label: 'Province', required: true, empty: 'e.g. Harare', maxlength: 50, autocomplete: 'address-level1',
        where: 'Billing address › State / Region on the Xero contact. A value typed here is kept in Fiscalize.' },
    ]
  },
];

export const ALL_FIELDS = BUYER_SECTIONS.flatMap((s) => s.fields);

/**
 * The form as it opens: every box shows the value in use, the one saved in Fiscalize or else Xero's own.
 * A value cleared in Fiscalize ("") stays empty rather than showing Xero's old value.
 */
export function draftFrom(saved: BuyerDetailsValues | null | undefined, xero?: BuyerDetailsValues | null): BuyerDraft {
  const d = { ...EMPTY_DRAFT };
  for (const k of BUYER_KEYS) {
    const s = saved?.[k];
    d[k] = (s != null ? s : xero?.[k] ?? '').toString();
  }
  return d;
}

/** Whether two values for a field are the same: TIN, VAT and phone by their digits, the rest ignoring case and spaces. */
export function sameValue(key: BuyerKey, a: string | null | undefined, b: string | null | undefined): boolean {
  const x = (a ?? '').trim(), y = (b ?? '').trim();
  if (!x || !y) return false;
  if (key === 'tin' || key === 'vatNumber' || key === 'phone') return x.replace(/\D/g, '') === y.replace(/\D/g, '');
  return x.replace(/\s+/g, ' ').toLowerCase() === y.replace(/\s+/g, ' ').toLowerCase();
}

const separators = /[\s.\-/]/g;

/** What is wrong with a typed value, using the same rules as the server and fiscalisation. */
export function fieldError(key: BuyerKey, value: string): string | null {
  const raw = value.trim();
  if (!raw) return null;
  const digits = raw.replace(separators, '');
  switch (key) {
    case 'tin':
      if (!/^\d{10}$/.test(digits)) return 'A TIN is exactly 10 digits.';
      if (/^0+$/.test(digits)) return '0000000000 is not a real TIN.';
      return null;
    case 'vatNumber':
      return /^\d{9}$/.test(digits) ? null : 'A VAT number is exactly 9 digits.';
    case 'phone': {
      const n = raw.replace(/[\s().\-]/g, '').replace(/^00/, '+');
      return /^\+?\d{7,15}$/.test(n) ? null : 'Use 7 to 15 digits, e.g. 0771234567 or +263771234567.';
    }
    case 'email':
      return /^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(raw) ? null : 'Enter a valid email, e.g. accounts@company.co.zw.';
    default:
      return null;
  }
}

/**
 * Payload for the save endpoint, per field:
 * - the same as Xero has it: null (nothing to change; "use Xero");
 * - emptied: "" (cleared), unless there was nothing to clear;
 * - anything else: the value. TIN/VAT lose separators.
 */
export function toSavePayload(d: BuyerDraft, xero?: BuyerDetailsValues | null, saved?: BuyerDetailsValues | null): SaveCustomerTaxDetails {
  const keep = (k: BuyerKey): string | null => {
    const v = d[k].trim();
    if (!v) return (xero?.[k] ?? '').toString().trim() || saved?.[k] != null ? '' : null;
    return sameValue(k, v, xero?.[k]) ? null : v;
  };
  const digits = (k: BuyerKey) => { const v = keep(k); return v == null ? null : v.replace(separators, ''); };
  return {
    registeredName: keep('registeredName'), tradeName: keep('tradeName'),
    tin: digits('tin'), vatNumber: digits('vatNumber'),
    phone: keep('phone'), email: keep('email'),
    houseNo: keep('houseNo'), street: keep('street'), city: keep('city'), province: keep('province'),
    updateXero: true
  };
}

/** Company-style initials: "Missing Bits (Pvt) Ltd" -> "MB" (legal suffixes and bracketed words are skipped). */
export function customerInitials(name: string | null | undefined): string {
  const skip = /^(pvt|private|ltd|limited|inc|llc|plc|co|company|t\/a|and|&|the|of)$/i;
  const words = (name || '').replace(/\([^)]*\)/g, ' ').split(/[\s.,]+/).filter((w) => w && !skip.test(w));
  if (!words.length) return (name || '?').trim().charAt(0).toUpperCase() || '?';
  return (words[0][0] + (words.length > 1 ? words[1][0] : '')).toUpperCase();
}

/** The two buyer values that are also written onto the Xero contact when saved; the rest stays in Fiscalize. */
export const WRITTEN_TO_XERO: BuyerKey[] = ['tin', 'vatNumber'];
