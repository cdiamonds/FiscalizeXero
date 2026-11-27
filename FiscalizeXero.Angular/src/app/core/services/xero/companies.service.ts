import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map, tap } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { XeroOrganisation, UserCompany } from '../../models/xero.models';
import { LoginResponse } from '../../models/auth.models';

export interface FiscalyzeCompany {
  id: number;
  name: string;
  tradeName: string | null;
  friendlyName: string | null;
  logo: string | null;
  role: string;
  isActive: boolean;
  accessCount: number;
  lastAccessedAt: string | null;
}

@Injectable({ providedIn: 'root' })
export class CompaniesService {
  private http = inject(HttpClient);
  private base = `${environment.apiURL}Users/me`;

  /** The connected Xero organisation, read from Xero. */
  currentOrganisation(): Observable<XeroOrganisation> {
    return this.http.get<XeroOrganisation>(`${environment.apiURL}xero/organisation`);
  }

  /**
   * Where "Open Xero" goes for the active company (its organisation, when Xero has told us its short code), or null
   * when it has no Xero connection. Kept current by every company-list read, so the top bar and pages share one answer.
   */
  readonly activeXeroUrl = signal<string | null>(null);

  myCompanies(): Observable<UserCompany[]> {
    return this.http.get<UserCompany[]>(`${this.base}/companies`).pipe(
      tap((rows) => this.activeXeroUrl.set((rows ?? []).find((c) => c.isActive)?.xeroUrl ?? null))
    );
  }

  /** The user's companies on the Fiscalize platform; search and paging run on the platform. */
  myFiscalyzeCompanies(search = '', pageSize = 500, page = 1): Observable<FiscalyzeCompany[]> {
    const params = new HttpParams().set('search', search).set('page', page).set('pageSize', pageSize);
    return this.http
      .get<{ value: FiscalyzeCompany[] }>(`${this.base}/fiscalize-companies`, { params })
      .pipe(map((r) => r?.value ?? []));
  }

  switch(companyId: number): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.base}/companies/${companyId}/switch`, null);
  }

  disconnect(companyId: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/companies/${companyId}`);
  }
}
