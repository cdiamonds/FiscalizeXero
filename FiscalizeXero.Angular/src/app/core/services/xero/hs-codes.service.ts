import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { PagedHsCodes, HsCodeLookupItem } from '../../models/xero.models';

/** Searches and validates codes against the Fiscalize HS code catalogue. */
@Injectable({ providedIn: 'root' })
export class HsCodesService {
  private http = inject(HttpClient);
  private base = `${environment.apiURL}HsCodes`;

  search(searchText?: string, page = 1, pageSize = 20): Observable<PagedHsCodes> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (searchText) params = params.set('search', searchText);
    return this.http.get<PagedHsCodes>(this.base, { params });
  }

  /** 200 with the catalogue row when the code exists, 404 otherwise. */
  validate(code: string): Observable<HsCodeLookupItem> {
    const params = new HttpParams().set('code', code);
    return this.http.get<HsCodeLookupItem>(`${this.base}/validate`, { params });
  }
}
