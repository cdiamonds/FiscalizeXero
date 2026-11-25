import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { CreditNote, DocumentPage, DocumentPageQuery } from '../../models/xero.models';

@Injectable({ providedIn: 'root' })
export class CreditNotesService {
  private http = inject(HttpClient);
  private base = `${environment.apiURL}xero/creditnotes`;

  /** Approved documents, most recently changed first. */
  list(since?: string, max = 100): Observable<CreditNote[]> {
    let params = new HttpParams().set('max', max);
    if (since) params = params.set('since', since);
    return this.http.get<CreditNote[]>(this.base, { params });
  }

  /** One page, newest first; Xero does the search (number, reference or contact name) and the status filter. */
  page(q: DocumentPageQuery): Observable<DocumentPage<CreditNote>> {
    let params = new HttpParams().set('page', q.page).set('pageSize', q.pageSize);
    if (q.search) params = params.set('search', q.search);
    if (q.status) params = params.set('status', q.status);
    return this.http.get<DocumentPage<CreditNote>>(`${this.base}/paged`, { params });
  }

  get(id: string): Observable<CreditNote> {
    return this.http.get<CreditNote>(`${this.base}/${id}`);
  }
}
