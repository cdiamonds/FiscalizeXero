import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';

export interface DashboardSummary {
  connection: {
    connected: boolean;
    tenantName: string | null;
    autoFiscalizeInvoices: boolean;
    autoFiscalizeCreditNotes: boolean;
    syncEnabled: boolean;
    syncIntervalHours: number;
    lastSyncAt: string | null;
    lastSyncError: string | null;
    webhooksConfigured: boolean;
    lastWebhookAt: string | null;
  };
  device: { deviceNumber: number; deviceId: number; environment: 'test' | 'production' | null } | null;
  counts: { today: number; last7Days: number; thisMonth: number; invoicesThisMonth: number; creditNotesThisMonth: number; total: number };
  daily: { date: string; invoices: number; creditNotes: number }[];
  failures: { documentId: string; documentType: 'invoice' | 'creditnote'; category: string; message: string; attempts: number; lastFailedAt: string }[];
  failureCount: number;
  recent: { documentId: string; documentType: 'invoice' | 'creditnote'; documentNumber: string; receiptNumber: number | null; verificationCode: string | null; fiscalizedAt: string }[];
}

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private http = inject(HttpClient);

  /** Read from the service's own database: opening the dashboard costs no Xero calls. */
  summary(): Observable<DashboardSummary> {
    return this.http.get<DashboardSummary>(`${environment.apiURL}dashboard/summary`);
  }
}
