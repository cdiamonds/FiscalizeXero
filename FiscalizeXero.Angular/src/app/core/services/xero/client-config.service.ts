import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  ClientConfig,
  DisconnectResult,
  RegisterClientResponse,
  FiscalizationSettings,
  FiscalLayoutStatus,
  XeroAppConfig,
  ConnectionManagement
} from '../../models/xero.models';

@Injectable({ providedIn: 'root' })
export class ClientConfigService {
  private http = inject(HttpClient);
  private base = `${environment.apiURL}ClientConfigs`;

  /** Who manages the connection, and whether the signed-in person may change it. */
  management(): Observable<ConnectionManagement> {
    return this.http.get<ConnectionManagement>(`${this.base}/management`);
  }

  current(): Observable<ClientConfig> {
    return this.http.get<ClientConfig>(`${this.base}/current`);
  }

  /** Disconnect Xero for the active company (the company stays on the account). */
  reset(): Observable<DisconnectResult> {
    return this.http.delete<DisconnectResult>(`${this.base}/current`);
  }

  /** Disconnect Xero for any of the user's companies. Null body (204) = already disconnected. */
  disconnectCompany(companyId: number): Observable<DisconnectResult | null> {
    return this.http.delete<DisconnectResult | null>(`${this.base}/by-company/${companyId}`);
  }

  /** Starts a connection for the active company: the answer holds the Xero consent address to open. */
  register(): Observable<RegisterClientResponse> {
    return this.http.post<RegisterClientResponse>(`${this.base}/register`, null);
  }

  reauth(): Observable<RegisterClientResponse> {
    return this.http.post<RegisterClientResponse>(`${this.base}/reauth`, null);
  }

  getFiscalizationSettings(): Observable<FiscalizationSettings> {
    return this.http.get<FiscalizationSettings>(`${this.base}/fiscalization-settings`);
  }

  updateFiscalizationSettings(req: FiscalizationSettings): Observable<void> {
    return this.http.patch<void>(`${this.base}/fiscalization-settings`, req);
  }

  /** The Fiscalize layout in use for invoices and credit notes; with refresh the platform is asked now. */
  getFiscalLayouts(refresh = false): Observable<FiscalLayoutStatus[]> {
    return this.http.get<FiscalLayoutStatus[]>(`${this.base}/fiscal-layouts`, { params: { refresh } });
  }

  /** Public facts about the server's Xero app, and whether it is ready to connect. */
  getXeroAppConfig(): Observable<XeroAppConfig> {
    return this.http.get<XeroAppConfig>(`${this.base}/xero-app-config`);
  }
}
