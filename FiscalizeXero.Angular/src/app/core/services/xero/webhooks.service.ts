import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { WebhookStatus } from '../../models/xero.models';

@Injectable({ providedIn: 'root' })
export class WebhooksService {
  private http = inject(HttpClient);
  private base = `${environment.apiURL}xero/webhooks`;

  /** Setup details plus the delivery log for the active company's Xero organisation. */
  status(): Observable<WebhookStatus> {
    return this.http.get<WebhookStatus>(`${this.base}/status`);
  }
}
