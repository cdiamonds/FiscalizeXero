import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map, shareReplay } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { TaxRateInfo } from '../../models/xero.models';

/** The organisation's Xero tax rates with their effective percentage, keyed by tax type and cached for the session. */
@Injectable({ providedIn: 'root' })
export class TaxRatesService {
  private http = inject(HttpClient);
  private cache$?: Observable<Record<string, TaxRateInfo>>;

  rates(): Observable<Record<string, TaxRateInfo>> {
    this.cache$ ??= this.http.get<TaxRateInfo[]>(`${environment.apiURL}xero/taxrates`).pipe(
      map((list) => Object.fromEntries((list ?? []).map((t) => [t.taxType, t]))),
      shareReplay(1)
    );
    return this.cache$;
  }
}
