import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  FiscalizeResult, FiscalCancelResult, UpdateInvoiceFiscalInfoRequest, CreditNoteBaseDocument,
  FiscalAttachmentStatus, FiscalFailure
} from '../../models/xero.models';

/** The API's route segment for each document type. */
export type FiscalDocumentRoute = 'invoices' | 'creditnotes';

@Injectable({ providedIn: 'root' })
export class FiscalizationService {
  private http = inject(HttpClient);
  private base = `${environment.apiURL}fiscalization`;

  // ── Invoices ──

  fiscalizeInvoice(invoiceId: string): Observable<FiscalizeResult> {
    return this.http.post<FiscalizeResult>(`${this.base}/invoices/${invoiceId}`, null);
  }

  /** The stored fiscal record for an invoice; 404 when it has not been fiscalized. */
  invoiceRecord(invoiceId: string): Observable<FiscalizeResult> {
    return this.http.get<FiscalizeResult>(`${this.base}/invoices/${invoiceId}/record`);
  }

  /** Credits the original FDMS record and issues a fresh one. */
  refiscalizeInvoice(invoiceId: string): Observable<FiscalizeResult> {
    return this.http.post<FiscalizeResult>(`${this.base}/invoices/${invoiceId}/refiscalize`, null);
  }

  /** Applies corrected buyer details (or removes the buyer) and re-fiscalizes. */
  updateInvoiceFiscalInfo(invoiceId: string, payload: UpdateInvoiceFiscalInfoRequest): Observable<FiscalizeResult> {
    return this.http.put<FiscalizeResult>(`${this.base}/invoices/${invoiceId}/fiscal-info`, payload);
  }

  /** Reverses the invoice at FDMS and removes its fiscal record. */
  fiscalCancelInvoice(invoiceId: string): Observable<FiscalCancelResult> {
    return this.http.post<FiscalCancelResult>(`${this.base}/invoices/${invoiceId}/fiscal-cancel`, null);
  }

  /** The Fiscalize platform's own PDF of the fiscal invoice. */
  fiscalizeInvoicePdf(invoiceId: string): Observable<Blob> {
    return this.http.get(`${this.base}/invoices/${invoiceId}/fiscalize-pdf`, { responseType: 'blob' });
  }

  /** Xero's own invoice PDF with the ZIMRA stamp drawn on it. 404 until the invoice is fiscalized. */
  stampedInvoicePdf(invoiceId: string): Observable<Blob> {
    return this.http.get(`${this.base}/invoices/${invoiceId}/pdf`, { responseType: 'blob' });
  }

  // ── Credit notes ──

  /** @param baseDocNo the invoice the credit note reverses, when it is not allocated in Xero or to choose another. */
  fiscalizeCreditNote(creditNoteId: string, baseDocNo?: string | null): Observable<FiscalizeResult> {
    return this.http.post<FiscalizeResult>(`${this.base}/creditnotes/${creditNoteId}`, { baseDocNo: baseDocNo || null });
  }

  /** Credits the original fiscal credit note and issues a fresh one. */
  refiscalizeCreditNote(creditNoteId: string, baseDocNo?: string | null): Observable<FiscalizeResult> {
    return this.http.post<FiscalizeResult>(`${this.base}/creditnotes/${creditNoteId}/refiscalize`, { baseDocNo: baseDocNo || null });
  }

  creditNoteRecord(creditNoteId: string): Observable<FiscalizeResult> {
    return this.http.get<FiscalizeResult>(`${this.base}/creditnotes/${creditNoteId}/record`);
  }

  /** The invoice a credit note reverses (from its Xero allocations), and the fiscalised invoices to choose from. */
  creditNoteBaseDocument(creditNoteId: string): Observable<CreditNoteBaseDocument> {
    return this.http.get<CreditNoteBaseDocument>(`${this.base}/creditnotes/${creditNoteId}/base-document`);
  }

  fiscalizeCreditNotePdf(creditNoteId: string): Observable<Blob> {
    return this.http.get(`${this.base}/creditnotes/${creditNoteId}/fiscalize-pdf`, { responseType: 'blob' });
  }

  stampedCreditNotePdf(creditNoteId: string): Observable<Blob> {
    return this.http.get(`${this.base}/creditnotes/${creditNoteId}/pdf`, { responseType: 'blob' });
  }

  // ── Both ──

  /** Whether the stamped fiscal PDF is attached to the document in Xero. */
  /** The document drawn with the company's Fiscalize layout (or Fiscalize's standard design). */
  layoutPdf(type: FiscalDocumentRoute, id: string, paper?: string): Observable<Blob> {
    const params: Record<string, string> = paper ? { paper } : {};
    return this.http.get(`${this.base}/${type}/${id}/layout-pdf`, { responseType: 'blob', params });
  }

  attachmentStatus(type: FiscalDocumentRoute, id: string): Observable<FiscalAttachmentStatus> {
    return this.http.get<FiscalAttachmentStatus>(`${this.base}/${type}/${id}/attachment`);
  }

  attachNow(type: FiscalDocumentRoute, id: string): Observable<FiscalAttachmentStatus> {
    return this.http.post<FiscalAttachmentStatus>(`${this.base}/${type}/${id}/attachment`, null);
  }

  /** Records for a page of documents; documents without one are absent from the answer. */
  records(type: FiscalDocumentRoute, ids: string[]): Observable<FiscalizeResult[]> {
    if (!ids.length) return of([]);
    const params = new HttpParams().set('type', type).set('ids', ids.join(','));
    return this.http.get<FiscalizeResult[]>(`${this.base}/records`, { params });
  }

  /** Latest failures for a page of documents; documents that never failed (or fiscalised since) are absent. */
  failures(type: FiscalDocumentRoute, ids: string[]): Observable<FiscalFailure[]> {
    if (!ids.length) return of([]);
    const params = new HttpParams().set('type', type).set('ids', ids.join(','));
    return this.http.get<FiscalFailure[]>(`${this.base}/failures`, { params });
  }

  /** Why one document last failed to fiscalise, or null when it hasn't (the API answers 204). */
  failure(route: FiscalDocumentRoute, id: string): Observable<FiscalFailure | null> {
    return this.http.get<FiscalFailure | null>(`${this.base}/${route}/${id}/failure`).pipe(map((f) => f ?? null));
  }
}
