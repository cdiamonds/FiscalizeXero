import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, catchError, map, shareReplay, throwError } from 'rxjs';
import { environment } from '../../../../environments/environment';

/**
 * QR code images drawn by the API (same library as the stamped PDF), cached per link for the session so moving
 * between documents never redraws one. A failed request is not cached, so a retry asks again.
 */
@Injectable({ providedIn: 'root' })
export class QrCodeService {
  private http = inject(HttpClient);
  private base = `${environment.apiURL}fiscalization/qr`;
  private cache = new Map<string, Observable<string>>();

  /** A `data:image/png;base64,...` URI for the link. */
  image(link: string): Observable<string> {
    const key = link.trim();
    let cached = this.cache.get(key);
    if (!cached) {
      cached = this.http.get<{ dataUri: string }>(this.base, { params: new HttpParams().set('data', key) }).pipe(
        map((r) => {
          if (!r?.dataUri?.startsWith('data:image/png;base64,')) throw new Error('Unexpected QR response');
          return r.dataUri;
        }),
        catchError((err) => { this.cache.delete(key); return throwError(() => err); }),
        shareReplay({ bufferSize: 1, refCount: false })
      );
      this.cache.set(key, cached);
    }
    return cached;
  }
}
