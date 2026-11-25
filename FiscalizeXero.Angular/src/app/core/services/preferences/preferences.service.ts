import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';

export type FiscalStampPosition =
  | 'top-left' | 'top-center' | 'top-right'
  | 'bottom-left' | 'bottom-center' | 'bottom-right';

export interface StampPrefs {
  fiscalStampEnabled: boolean;
  fiscalStampPosition: FiscalStampPosition;
  fiscalStampMarginTop: number;
  fiscalStampMarginBottom: number;
  fiscalStampMarginLeft: number;
  fiscalStampMarginRight: number;
}

@Injectable({ providedIn: 'root' })
export class PreferencesService {
  private http = inject(HttpClient);
  private base = `${environment.apiURL}Users/me/preferences`;

  get(): Observable<{ theme: string }> {
    return this.http.get<{ theme: string }>(this.base);
  }

  save(theme: 'light' | 'dark'): Observable<void> {
    return this.http.put<void>(this.base, { theme });
  }

  getStamp(): Observable<StampPrefs> {
    return this.http.get<StampPrefs>(this.base);
  }

  saveStamp(prefs: Partial<StampPrefs>): Observable<void> {
    return this.http.put<void>(this.base, prefs);
  }
}
