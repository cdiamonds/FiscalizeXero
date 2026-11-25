import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ClientConfigService } from '../../core/services/xero/client-config.service';
import { ConnectionManagement } from '../../core/models/xero.models';

/**
 * Who manages this company's Xero connection. The person who connected Xero manages it (disconnect, fiscalisation
 * settings, device keys); a colleague sees who to ask, and that reconnecting with their own Xero login hands it to them.
 */
@Component({
  selector: 'app-manage-notice',
  imports: [RouterLink],
  template: `
    @if (info(); as m) {
      @if (!m.canManage) {
        <div class="mb-4 flex flex-wrap items-start gap-3 rounded-2xl border border-amber-200 bg-amber-50 p-4 text-sm text-amber-900 sm:flex-nowrap sm:items-center dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-100" role="status" data-testid="manage-notice">
          <svg class="h-5 w-5 shrink-0 text-amber-600 dark:text-amber-300" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="5" y="11" width="14" height="10" rx="2"/><path d="M8 11V7a4 4 0 018 0v4"/></svg>
          <p class="min-w-0 flex-1 basis-[12rem]">
            <span class="font-semibold">{{ m.connectedByName || 'A colleague' }} manages this Xero connection.</span>
            You can view everything here; to change it, ask them, or reconnect Xero with your own Xero login to manage it yourself.
          </p>
          @if (showReconnect) {
            <a routerLink="/xero-connection" class="ml-8 inline-flex shrink-0 items-center justify-center rounded-lg border border-amber-300 sm:ml-0 bg-white px-3 py-1.5 text-xs font-semibold text-amber-900 hover:bg-amber-100 dark:border-amber-500/40 dark:bg-transparent dark:text-amber-100 dark:hover:bg-amber-500/15">Xero connection</a>
          }
        </div>
      } @else if (m.isYou && showOwner) {
        <p class="mb-4 text-xs text-slate-500 dark:text-slate-400" data-testid="manage-owner">You connected Xero for this company, so you manage the connection, settings and device keys.</p>
      }
    }
  `,
})
export class ManageNotice implements OnInit {
  /** Offer the link to the Xero Connection page (off on that page itself). */
  @Input() showReconnect = true;
  /** Say "you manage this" to the person who connected (shown on the Xero Connection page). */
  @Input() showOwner = false;

  private api = inject(ClientConfigService);
  readonly info = signal<ConnectionManagement | null>(null);

  ngOnInit(): void {
    this.api.management().subscribe({ next: (m) => this.info.set(m), error: () => this.info.set(null) });
  }
}
