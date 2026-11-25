import { ManageNotice } from '../../components/manage-notice/manage-notice';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { ClientConfigService } from '../../core/services/xero/client-config.service';
import { CompaniesService } from '../../core/services/xero/companies.service';
import { AuthService } from '../../core/services/auth/auth.service';
import { ClientConfig, XeroOrganisation, DisconnectResult, UserCompany } from '../../core/models/xero.models';
import { ToastService } from '../../core/services/toast/toast.service';
import { extractErrorMessage } from '../../core/utils/http-error';
import { asUtc } from '../../core/utils/xero-format';
import { PageHeader } from '../../components/page-header/page-header';
import { ConfirmDialog } from '../../components/confirm-dialog/confirm-dialog';
import { CompanyList } from '../../components/company-list/company-list';

@Component({
  selector: 'app-xero-connection',
  imports: [CommonModule, ManageNotice, PageHeader, ConfirmDialog, CompanyList],
  template: `
    <app-page-header title="Xero connection" subtitle="Manage how this company connects to its Xero organisation." />
    <app-manage-notice [showReconnect]="false" [showOwner]="true" />

    <div class="space-y-4">
      <div *ngIf="loading()" class="flex justify-center py-10">
        <div class="h-8 w-8 animate-spin rounded-full border-4 border-brand-200 border-t-primary-500"></div>
      </div>

      <!-- Connection -->
      <section *ngIf="!loading() && config() as cfg" class="rounded-2xl border border-white/40 bg-white/70 p-6 shadow-sm backdrop-blur-md fade-in dark:border-white/10 dark:bg-slate-900/60">
        <div class="mb-4 flex items-start justify-between gap-3">
          <div class="min-w-0">
            <div class="flex items-center gap-2">
              <span class="inline-flex h-2 w-2 flex-shrink-0 rounded-full"
                    [class.bg-emerald-500]="health() === 'connected'"
                    [class.bg-amber-500]="health() !== 'connected'"></span>
              <h2 class="text-base font-semibold text-slate-900 dark:text-white" data-testid="connection-state">
                {{ health() === 'connected' ? 'Connected to Xero' : health() === 'reconnect' ? 'Xero needs to be reconnected' : 'Waiting for you to allow access in Xero' }}
              </h2>
            </div>
            <p *ngIf="cfg.tenantName" class="mt-1 truncate text-sm text-slate-700 dark:text-slate-200" data-testid="tenant-name">{{ cfg.tenantName }}</p>
            <p *ngIf="health() === 'reconnect'" class="mt-1 text-xs text-amber-700 dark:text-amber-300">{{ problem() }}</p>
          </div>
          <a *ngIf="xeroUrl() && health() === 'connected'" [href]="xeroUrl()" target="_blank" rel="noopener"
             class="inline-flex flex-shrink-0 items-center gap-1.5 rounded-xl border border-slate-200 bg-white px-3 py-1.5 text-xs font-semibold text-slate-700 hover:bg-slate-50 dark:border-white/10 dark:bg-slate-900 dark:text-slate-200 dark:hover:bg-white/5">
            Open Xero
            <svg class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 13v6a2 2 0 01-2 2H5a2 2 0 01-2-2V8a2 2 0 012-2h6M15 3h6v6M10 14L21 3"/></svg>
          </a>
        </div>

        <dl class="space-y-2 text-sm">
          <div class="flex justify-between gap-3"><dt class="text-slate-500">Xero organisation</dt><dd class="truncate text-right text-slate-900 dark:text-white">{{ cfg.tenantName || '—' }}</dd></div>
          <div class="flex justify-between gap-3"><dt class="text-slate-500">Organisation ID</dt><dd class="break-all text-right font-mono text-xs text-slate-700 dark:text-slate-300">{{ cfg.tenantId || '—' }}</dd></div>
          <div *ngIf="cfg.tenantId" class="flex justify-between gap-3" data-testid="last-sync"><dt class="text-slate-500">Last check for missed documents</dt><dd class="text-right text-slate-700 dark:text-slate-300">{{ cfg.lastSyncAt ? (utc(cfg.lastSyncAt) | date:'medium') : 'Not yet' }}</dd></div>
          <div class="flex justify-between gap-3"><dt class="text-slate-500">Used by</dt><dd class="text-right text-slate-700 dark:text-slate-300">Everyone in this company</dd></div>
        </dl>

        <div *ngIf="organisation() as org" data-testid="organisation" class="mt-4 rounded-xl border border-emerald-200 bg-emerald-50/60 p-4 text-sm dark:border-emerald-500/30 dark:bg-emerald-500/10">
          <div class="flex items-center gap-2 font-semibold text-emerald-900 dark:text-emerald-100">
            <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M5 13l4 4L19 7"/></svg>
            Xero answered: {{ org.name }}
            <span *ngIf="org.isDemoCompany" class="rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-bold uppercase text-amber-800 dark:bg-amber-500/20 dark:text-amber-200">Demo company</span>
          </div>
          <div class="mt-1 text-xs text-emerald-800/80 dark:text-emerald-200/80">
            <ng-container *ngIf="org.legalName && org.legalName !== org.name">Legal name: {{ org.legalName }} · </ng-container>
            Base currency {{ org.baseCurrency || '—' }}<ng-container *ngIf="org.countryCode"> · {{ org.countryCode }}</ng-container><ng-container *ngIf="org.taxNumber"> · Tax number {{ org.taxNumber }}</ng-container>
          </div>
        </div>
        <div *ngIf="checkError() as e" role="alert" data-testid="xero-attention"
             class="mt-4 rounded-xl border border-amber-200 bg-amber-50 p-4 text-sm text-amber-800 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-200">
          <div class="font-semibold">Xero needs attention</div>
          <div class="mt-0.5">{{ e }}</div>
        </div>

        <div class="mt-5 flex flex-wrap items-center gap-2">
          <button type="button" (click)="reauth()" [disabled]="busy()" data-testid="reconnect"
                  class="inline-flex items-center gap-1.5 rounded-xl bg-gradient-to-r from-primary-500 to-primary-600 px-4 py-2 text-xs font-semibold text-white shadow-md shadow-primary-500/20 transition-all duration-200 hover:from-primary-600 hover:to-primary-700 hover:scale-[1.03] disabled:opacity-50 disabled:hover:scale-100">
            <svg *ngIf="busy()" class="h-3.5 w-3.5 animate-spin" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
            {{ health() === 'connected' ? 'Reconnect' : 'Connect to Xero' }}
          </button>
          <button *ngIf="health() === 'connected'" type="button" (click)="checkConnection()" [disabled]="checking()" data-testid="check-connection"
                  class="inline-flex items-center gap-1.5 rounded-xl border border-slate-200 bg-white px-4 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-50 disabled:opacity-50 dark:border-white/10 dark:bg-slate-900 dark:text-slate-200 dark:hover:bg-white/5">
            <svg *ngIf="checking()" class="h-3.5 w-3.5 animate-spin" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
            Check connection
          </button>
          <button type="button" (click)="askDisconnectActive()" [disabled]="busy()"
                  class="inline-flex items-center gap-1.5 rounded-xl border border-rose-200 bg-white px-4 py-2 text-xs font-semibold text-rose-600 hover:bg-rose-50 disabled:opacity-50 dark:border-rose-500/30 dark:bg-rose-500/10 dark:text-rose-300 dark:hover:bg-rose-500/15">
            Disconnect
          </button>
        </div>
        <p class="mt-3 text-[11px] text-slate-500 dark:text-slate-400">Reconnect lets you allow access again, or choose a different Xero organisation for this company.</p>
      </section>

      <!-- Not yet connected -->
      <section *ngIf="!loading() && !config()" class="rounded-2xl border border-white/40 bg-white/70 p-6 shadow-sm backdrop-blur-md fade-in dark:border-white/10 dark:bg-slate-900/60">
        <h2 class="text-base font-semibold text-slate-900 dark:text-white">No Xero connection yet</h2>
        <p class="mt-1 text-sm text-slate-500 dark:text-slate-400">Connect a Xero organisation to start fiscalizing its invoices and credit notes.</p>
        <button type="button" (click)="goRegister()"
                class="mt-4 inline-flex items-center gap-2 rounded-xl bg-gradient-to-r from-primary-500 to-primary-600 px-4 py-2 text-sm font-semibold text-white shadow-md shadow-primary-500/25 transition-all duration-200 hover:from-primary-600 hover:to-primary-700 hover:scale-[1.03]">
          Connect Xero
          <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14M13 5l7 7-7 7"/></svg>
        </button>
      </section>

      <!-- All companies on the account: switch, connect, disconnect -->
      <section *ngIf="!loading()" class="rounded-2xl border border-white/40 bg-white/70 p-6 shadow-sm backdrop-blur-md fade-in dark:border-white/10 dark:bg-slate-900/60">
        <div class="mb-4">
          <h2 class="text-base font-semibold text-slate-900 dark:text-white">Your companies</h2>
          <p class="mt-1 text-xs text-slate-500 dark:text-slate-400">
            Each company has its own Xero organisation. Switch between them, connect a new one, or disconnect one you no longer fiscalize.
          </p>
        </div>
        <div *ngIf="companiesLoading()" class="flex justify-center py-6">
          <div class="h-6 w-6 animate-spin rounded-full border-4 border-brand-200 border-t-primary-500"></div>
        </div>
        <app-company-list *ngIf="!companiesLoading()"
                          [companies]="companies()"
                          [busyId]="rowBusyId()"
                          [busyAction]="rowBusyAction()"
                          (switch)="switchTo($event)"
                          (connect)="connectCompany($event)"
                          (disconnect)="askDisconnect($event)" />
      </section>
    </div>

    <app-confirm-dialog
      [open]="!!pendingDisconnect()"
      variant="danger"
      title="Disconnect from Xero?"
      [description]="disconnectDescription()"
      confirmLabel="Yes, disconnect"
      busyLabel="Disconnecting…"
      cancelLabel="Keep connected"
      [busy]="rowBusyAction() === 'disconnect'"
      (confirm)="confirmDisconnect()"
      (cancel)="cancelDisconnect()" />
  `
})
export class XeroConnection implements OnInit {
  private clientConfigService = inject(ClientConfigService);
  private companiesService = inject(CompaniesService);
  private authService = inject(AuthService);
  private router = inject(Router);
  private toast = inject(ToastService);

  readonly config = signal<ClientConfig | null>(null);
  /** What Xero answered to "Check connection". */
  readonly organisation = signal<XeroOrganisation | null>(null);
  readonly checking = signal(false);
  readonly checkError = signal<string | null>(null);
  readonly loading = signal(true);
  readonly busy = signal(false);

  readonly companies = signal<UserCompany[]>([]);
  readonly companiesLoading = signal(true);
  readonly pendingDisconnect = signal<UserCompany | null>(null);
  readonly rowBusyId = signal<number | null>(null);
  readonly rowBusyAction = signal<'switch' | 'disconnect' | 'connect' | null>(null);

  readonly disconnectDescription = (): string => {
    const c = this.pendingDisconnect();
    if (!c) return '';
    return `${c.companyName} will lose its Xero connection for everyone in the company, and Fiscalize will be removed from that organisation's connected apps in Xero. ` +
      `Fiscal records already created are kept, but invoices and credit notes won't be fiscalized until you connect again. ` +
      `The company stays on your account.`;
  };

  ngOnInit(): void {
    this.loadActive();
    this.loadCompanies();
  }

  private loadActive(): void {
    this.loading.set(true);
    this.clientConfigService.current().subscribe({
      next: (cfg) => {
        this.config.set(cfg);
        this.loading.set(false);
      },
      error: () => { this.config.set(null); this.organisation.set(null); this.loading.set(false); }
    });
  }

  private loadCompanies(): void {
    this.companiesLoading.set(true);
    this.companiesService.myCompanies().subscribe({
      next: (list) => { this.companies.set(list ?? []); this.companiesLoading.set(false); },
      error: (err) => {
        this.companiesLoading.set(false);
        this.toast.error(extractErrorMessage(err, 'Could not load your companies.'));
      }
    });
  }

  private readonly activeCompany = computed(() => this.companies().find((c) => c.isActive) ?? null);
  readonly xeroUrl = computed(() => this.activeCompany()?.xeroUrl ?? null);
  readonly problem = computed(() => this.activeCompany()?.connectionProblem || 'Xero stopped accepting this connection. Reconnect to carry on fiscalizing.');
  /** connected: usable. reconnect: an organisation was connected but Xero no longer accepts it. pending: consent never finished. */
  readonly health = computed<'connected' | 'reconnect' | 'pending'>(() => {
    const cfg = this.config();
    if (!cfg?.tenantId) return 'pending';
    const active = this.activeCompany();
    if (active && (active.needsReauth || !active.isSetupComplete)) return 'reconnect';
    return 'connected';
  });

  utc = asUtc;

  /** Asks Xero for the organisation, which proves the connection works end to end (one Xero call). */
  checkConnection(): void {
    if (this.checking()) return;
    this.checking.set(true);
    this.checkError.set(null);
    this.organisation.set(null);
    this.companiesService.currentOrganisation().subscribe({
      next: (org) => { this.organisation.set(org); this.checking.set(false); },
      error: (err) => {
        this.checking.set(false);
        this.checkError.set(extractErrorMessage(err, 'Xero could not be reached. Try again, or reconnect.'));
      }
    });
  }

  // ─── Re-authorize / connect ────────────────────────────────────────────────
  reauth(): void {
    this.busy.set(true);
    this.clientConfigService.reauth().subscribe({
      next: (res) => { if (res?.authUrl) window.location.href = res.authUrl; else { this.busy.set(false); this.toast.error('Xero did not return a sign-in address. Try again.'); } },
      error: (err) => { this.busy.set(false); this.toast.error(extractErrorMessage(err, 'Could not start the Xero sign-in.')); }
    });
  }

  goRegister(): void { this.router.navigate(['/register-client']); }

  /** Connect or re-authorize a company. Connection always acts on the active company, so switch first. */
  connectCompany(c: UserCompany): void {
    if (this.rowBusyId() !== null) return;
    if (c.isActive) { c.isSetupComplete ? this.reauth() : this.goRegister(); return; }

    this.rowBusyId.set(c.companyId);
    this.rowBusyAction.set('connect');
    this.companiesService.switch(c.companyId).subscribe({
      next: (res) => {
        this.authService.persistSession(res);
        if (c.isSetupComplete) {
          this.clientConfigService.reauth().subscribe({
            next: (r) => { window.location.href = r?.authUrl || '/xero-connection'; },
            error: (err) => { this.clearRowBusy(); this.toast.error(extractErrorMessage(err, 'Could not start the Xero sign-in.')); }
          });
        } else {
          window.location.href = '/register-client';
        }
      },
      error: (err) => { this.clearRowBusy(); this.toast.error(extractErrorMessage(err, `Could not switch to ${c.companyName}.`)); }
    });
  }

  // ─── Switch ────────────────────────────────────────────────────────────────
  switchTo(c: UserCompany): void {
    if (this.rowBusyId() !== null || c.isActive) return;
    this.rowBusyId.set(c.companyId);
    this.rowBusyAction.set('switch');
    this.companiesService.switch(c.companyId).subscribe({
      next: (res) => { this.authService.persistSession(res); window.location.reload(); },
      error: (err) => { this.clearRowBusy(); this.toast.error(extractErrorMessage(err, `Could not switch to ${c.companyName}.`)); }
    });
  }

  // ─── Disconnect ────────────────────────────────────────────────────────────
  askDisconnectActive(): void {
    const active = this.companies().find((c) => c.isActive);
    if (active) { this.pendingDisconnect.set(active); return; }
    // List not loaded (or failed): fall back to a minimal stand-in for the active company.
    const u = this.authService.currentUser();
    this.pendingDisconnect.set({
      id: 0, companyId: u?.companyId ?? 0, companyName: u?.company || 'This company', isActive: true,
      isSetupComplete: true, isUsingSharedConnection: false, needsReauth: false
    });
  }

  askDisconnect(c: UserCompany): void {
    if (this.rowBusyId() !== null) return;
    this.pendingDisconnect.set(c);
  }

  cancelDisconnect(): void {
    if (this.rowBusyAction() === 'disconnect') return; // not while in flight
    this.pendingDisconnect.set(null);
  }

  confirmDisconnect(): void {
    const c = this.pendingDisconnect();
    if (!c) return;
    this.rowBusyId.set(c.companyId);
    this.rowBusyAction.set('disconnect');

    // The stand-in used when the companies list failed to load has no id: fall back to "current".
    const request = c.companyId > 0
      ? this.clientConfigService.disconnectCompany(c.companyId)
      : this.clientConfigService.reset();
    request.subscribe({
      next: (res) => {
        this.clearRowBusy();
        this.pendingDisconnect.set(null);
        this.reportWarnings(res);

        if (c.isActive) {
          // The company the user is working in just lost its Xero link: send them to the
          // connect page, where they can connect again or pick another company.
          this.authService.markActiveCompanyDisconnected();
          this.router.navigate(['/register-client'], { queryParams: { disconnected: c.companyName } });
          return;
        }
        this.toast.success(`${c.companyName} disconnected from Xero.`);
        this.loadCompanies();
      },
      error: (err) => {
        this.clearRowBusy();
        this.pendingDisconnect.set(null);
        this.toast.error(extractErrorMessage(err, 'Could not disconnect.'));
      }
    });
  }

  private reportWarnings(res: DisconnectResult | null): void {
    const w = res?.warnings ?? [];
    if (w.length) this.toast.error(`Disconnected, but: ${w.join(' ')}`);
  }

  private clearRowBusy(): void {
    this.rowBusyId.set(null);
    this.rowBusyAction.set(null);
  }
}
