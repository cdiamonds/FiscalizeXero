import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { Invoice, DocumentPage, DocumentPageQuery } from '../../models/xero.models';

@Injectable({ providedIn: 'root' })
export class InvoicesService {
  private http = inject(HttpClient);
  private base = `${environment.apiURL}xero/invoices`;

  /** Approved documents, most recently changed first. */
  list(since?: string, max = 100): Observable<Invoice[]> {
    let params = new HttpParams().set('max', max);
    if (since) params = params.set('since', since);
    return this.http.get<Invoice[]>(this.base, { params });
  }

  /** One page, newest first; Xero does the search (number, reference or contact name) and the status filter. */
  page(q: DocumentPageQuery): Observable<DocumentPage<Invoice>> {
    let params = new HttpParams().set('page', q.page).set('pageSize', q.pageSize);
    if (q.search) params = params.set('search', q.search);
    if (q.status) params = params.set('status', q.status);
    return this.http.get<DocumentPage<Invoice>>(`${this.base}/paged`, { params });
  }

  get(id: string): Observable<Invoice> {
    return this.http.get<Invoice>(`${this.base}/${id}`);
  }
}
