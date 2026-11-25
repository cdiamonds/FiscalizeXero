import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { DocumentPage, DocumentPageQuery } from '../../models/xero.models';

export interface CustomerTaxDetails {
  /** The Xero contact id. */
  customerId: string;
  displayName?: string | null;
  registeredName?: string | null;
  tradeName?: string | null;
  vatNumber?: string | null;
  tin?: string | null;
  /** 'Override' when a value is saved in Fiscalize, otherwise 'Xero'. */
  source: string;
  /** The email that will be sent (first valid one on the contact). */
  email?: string | null;
  /** The phone that will be sent: Phone, else Mobile, else Direct dial. */
  phone?: string | null;
  phoneSource?: string | null;
  defaultPhone?: string | null;
  mobile?: string | null;
  directDial?: string | null;
  addressText?: string | null;
  /** Address parts as they will be sent to ZIMRA. */
  houseNo?: string | null;
  street?: string | null;
  city?: string | null;
  province?: string | null;
  /** Values typed in Fiscalize (each wins over Xero). Blank means "use Xero". */
  saved?: BuyerDetailsValues;
  /** What the Xero contact alone provides: the fallback for anything not typed in Fiscalize. */
  xero?: BuyerDetailsValues;
  /** What the contact's Tax number box holds in Xero. */
  xeroTaxNumber?: string | null;
  /** What the contact's Company number box holds in Xero. */
  xeroCompanyNumber?: string | null;
  /** Set on a save: true when the Xero contact was updated too. */
  xeroUpdated?: boolean | null;
  /** Set on a save when Xero didn't take (all of) the update; values are still saved in Fiscalize. */
  xeroWarning?: string | null;
  complianceStatus: CustomerComplianceStatus;
  /** Buyer details ZIMRA needs with a TIN that could not be found. */
  missing: string[];
  /** Values present but wrong (bad TIN/VAT, VAT without TIN). */
  problems: string[];
  /** The company's buyer validation: Strict blocks missing details, Lenient fills placeholders. */
  validationMode?: 'Strict' | 'Lenient' | null;
}

export type CustomerComplianceStatus = 'NoTin' | 'Compliant' | 'MissingDetails' | 'Invalid';

export interface CustomerComplianceCounts {
  all: number; compliant: number; missingDetails: number; invalid: number; noTin: number;
}

export interface CustomerOverview {
  items: CustomerTaxDetails[];
  total: number;
  page: number;
  pageSize: number;
  counts: CustomerComplianceCounts;
  validationMode: 'Strict' | 'Lenient';
  truncated: boolean;
}

/** The ten buyer values ZIMRA uses. */
export interface BuyerDetailsValues {
  registeredName?: string | null;
  tradeName?: string | null;
  vatNumber?: string | null;
  tin?: string | null;
  phone?: string | null;
  email?: string | null;
  houseNo?: string | null;
  street?: string | null;
  city?: string | null;
  province?: string | null;
}

/**
 * Buyer values to save. Null means "use Xero"; an empty string clears a value. With updateXero the TIN and VAT number
 * are also written onto the Xero contact (Tax number and Company number), and the rest stays in Fiscalize.
 */
export interface SaveCustomerTaxDetails extends BuyerDetailsValues {
  updateXero?: boolean;
}

export interface ItemHsCode {
  /** The Xero item code: what HS codes are saved against. */
  itemCode: string;
  itemId?: string | null;
  name?: string | null;
  description?: string | null;
  isSold?: boolean | null;
  hsCode?: string | null;
  /** "Saved" (kept in Fiscalize) or "Default" (the company's default HS code). */
  hsCodeSource?: 'Saved' | 'Default' | string | null;
  warning?: string | null;
}

@Injectable({ providedIn: 'root' })
export class TaxDetailsService {
  private http = inject(HttpClient);
  private customersBase = `${environment.apiURL}xero/customers`;
  private itemsBase = `${environment.apiURL}xero/items`;

  listCustomers(search?: string, page = 1, pageSize = 50): Observable<CustomerTaxDetails[]> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (search) params = params.set('search', search);
    return this.http.get<CustomerTaxDetails[]>(this.customersBase, { params });
  }

  /** Every active customer with compliance, filtered (search, status) and paged, plus counts per status. */
  customerOverview(opts: { search?: string; status?: CustomerComplianceStatus | 'Attention' | ''; page?: number; pageSize?: number; refresh?: boolean }): Observable<CustomerOverview> {
    let params = new HttpParams().set('page', opts.page ?? 1).set('pageSize', opts.pageSize ?? 25);
    if (opts.search) params = params.set('search', opts.search);
    if (opts.status) params = params.set('status', opts.status);
    if (opts.refresh) params = params.set('refresh', true);
    return this.http.get<CustomerOverview>(`${this.customersBase}/overview`, { params });
  }

  getCustomer(id: string): Observable<CustomerTaxDetails> {
    return this.http.get<CustomerTaxDetails>(`${this.customersBase}/${id}`);
  }

  saveCustomer(id: string, body: SaveCustomerTaxDetails): Observable<CustomerTaxDetails> {
    return this.http.put<CustomerTaxDetails>(`${this.customersBase}/${id}/tax-details`, body);
  }

  /** One page of items by name with their HS codes, and the total. */
  itemsPage(q: DocumentPageQuery): Observable<DocumentPage<ItemHsCode>> {
    let params = new HttpParams().set('page', q.page).set('pageSize', q.pageSize);
    if (q.search) params = params.set('search', q.search);
    if (q.refresh) params = params.set('refresh', true);
    return this.http.get<DocumentPage<ItemHsCode>>(`${this.itemsBase}/paged`, { params });
  }

  listItems(search?: string, page = 1, pageSize = 50): Observable<ItemHsCode[]> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (search) params = params.set('search', search);
    return this.http.get<ItemHsCode[]>(this.itemsBase, { params });
  }

  /** HS code in use for each of the given Xero item codes, keyed by item code. */
  hsCodesFor(itemCodes: string[]): Observable<Record<string, ItemHsCode>> {
    let params = new HttpParams();
    for (const code of itemCodes) params = params.append('codes', code);
    return this.http.get<Record<string, ItemHsCode>>(`${this.itemsBase}/hs-codes`, { params });
  }

  /** Saves the HS code for a Xero item code. An empty code goes back to the company's default. */
  saveItemHsCode(itemCode: string, hsCode: string | null): Observable<ItemHsCode> {
    return this.http.put<ItemHsCode>(`${this.itemsBase}/hs-code`, { itemCode, hsCode });
  }
}
