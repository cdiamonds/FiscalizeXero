import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { InvoicesService } from '../../core/services/xero/invoices.service';
import { FiscalizationService } from '../../core/services/xero/fiscalization.service';
import { Invoice, FiscalFailure } from '../../core/models/xero.models';
import { byDocumentId } from '../../core/utils/fiscal-failure';
import { FiscalCacheService } from '../../core/services/fiscal-cache/fiscal-cache.service';
import { ToastService } from '../../core/services/toast/toast.service';
import { extractErrorMessage, isXeroAttention } from '../../core/utils/http-error';
import { PageHeader } from '../../components/page-header/page-header';
import { SearchBar } from '../../components/search-bar/search-bar';
import { FiscalPill } from '../../components/fiscal-pill/fiscal-pill';
import { Pagination } from '../../components/pagination/pagination';
import { DocumentList, PAGE_SIZES } from '../../core/utils/document-list';
import { STATUS_FILTERS, calendarDate, isApproved, statusClasses, statusLabel } from '../../core/utils/xero-format';

@Component({
  selector: 'app-invoices',
  imports: [CommonModule, RouterLink, PageHeader, SearchBar, FiscalPill, Pagination],
  template: `
    <app-page-header title="Invoices" subtitle="Sales invoices from your Xero organisation. Approved invoices are submitted to ZIMRA in one click." />

    <!-- Search + filter card -->
    <div class="mb-4 rounded-2xl border border-white/40 bg-white/70 p-3 shadow-sm backdrop-blur-sm slide-down dark:border-white/10 dark:bg-slate-900/60">
      <div class="flex flex-col gap-2 sm:flex-row sm:items-center sm:gap-3">
        <app-search-bar class="flex-1 min-w-0 block" [initialValue]="list.search()"
                        placeholder="Search by invoice number, reference or customer…"
                        (valueChange)="onSearch($event)"></app-search-bar>
        <button type="button" (click)="reload()" [disabled]="list.loading()"
                class="inline-flex items-center justify-center rounded-xl border border-slate-200 bg-white/70 px-3.5 py-2.5 text-slate-600 transition-all duration-200 hover:bg-white hover:scale-[1.02] focus:ring-2 focus:ring-primary-500/30 disabled:cursor-not-allowed disabled:opacity-50 dark:border-white/10 dark:bg-slate-950/60 dark:text-slate-300 dark:hover:bg-white/5"
                title="Refresh" aria-label="Refresh">
          <svg class="h-5 w-5" [class.animate-spin]="list.loading()" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"/></svg>
        </button>
      </div>
      <!-- Status filter: Xero's own statuses. Only approved documents can be fiscalized. -->
      <div class="mt-2 flex gap-1.5 overflow-x-auto pb-0.5" role="group" aria-label="Filter by Xero status" data-testid="status-filter">
        <button *ngFor="let f of statusFilters" type="button" (click)="list.setStatus(f.value)"
                [attr.aria-pressed]="list.status() === f.value"
                [class]="'flex-shrink-0 rounded-full px-3 py-1 text-xs font-semibold transition-colors ' +
                  (list.status() === f.value
                    ? 'bg-primary-500 text-white shadow-sm'
                    : 'bg-slate-100 text-slate-600 hover:bg-slate-200 dark:bg-white/10 dark:text-slate-300 dark:hover:bg-white/15')">
          {{ f.label }}
        </button>
      </div>
    </div>

    <div *ngIf="!loading() && list.loaded()" class="mb-3 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-slate-500 dark:text-slate-400 fade-in">
      <span class="inline-flex items-center gap-1.5">
        <svg class="h-4 w-4 text-slate-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 6h13M3 12h9M3 18h5M17 8v12m0 0-3-3m3 3 3-3"/></svg>
        Newest first
      </span>
      <span *ngIf="list.search()" class="font-medium text-primary-600 dark:text-brand-400">{{ list.total() | number }} {{ list.total() === 1 ? 'match' : 'matches' }} for “{{ list.search() }}”</span>
      <span *ngIf="!list.status()" class="text-xs">Showing approved invoices (awaiting payment and paid). Drafts are fiscalized once approved in Xero.</span>
    </div>

    <div *ngIf="loading()" class="flex justify-center py-12">
      <div class="h-8 w-8 animate-spin rounded-full border-4 border-brand-200 border-t-primary-500"></div>
    </div>

    <!-- Xero refused or needs reconnecting: say so here, with the way to fix it. -->
    <div *ngIf="xeroProblem() as problem" role="alert" data-testid="xero-attention"
         class="mb-4 flex flex-col gap-3 rounded-2xl border border-amber-200 bg-amber-50 p-4 text-sm text-amber-900 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-100 sm:flex-row sm:items-center sm:justify-between">
      <div>
        <div class="font-semibold">Xero needs attention</div>
        <div class="mt-0.5">{{ problem }}</div>
      </div>
      <a routerLink="/xero-connection" class="inline-flex flex-shrink-0 items-center justify-center rounded-xl bg-amber-600 px-4 py-2 text-xs font-semibold text-white hover:bg-amber-700">Open Xero connection</a>
    </div>

    <ng-container *ngIf="!loading() && !(list.failed() && rows().length === 0)">
      <div class="transition-opacity duration-200" [class.opacity-60]="list.loading()" [attr.aria-busy]="list.loading()">
      <div *ngIf="rows().length === 0" class="rounded-2xl border border-white/40 bg-white/70 p-12 text-center shadow-sm backdrop-blur-sm empty-state-fade-in dark:border-white/10 dark:bg-slate-900/60">
        <svg class="mx-auto mb-4 h-16 w-16 text-slate-400" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 17v-2m3 2v-4m3 4v-6m2 10H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"/></svg>
        <h3 class="mb-2 text-lg font-medium text-slate-900 dark:text-white">No invoices found</h3>
        <p class="text-slate-600 dark:text-slate-400">{{ list.search() ? 'Try a different search, or another status.' : (list.status() ? 'No invoices with this status in the Xero organisation.' : 'No approved invoices in this Xero organisation yet.') }}</p>
      </div>

      <!-- Desktop table -->
      <div *ngIf="rows().length > 0" class="mb-4 hidden overflow-hidden rounded-2xl border border-white/40 bg-white/70 shadow-sm backdrop-blur-sm table-slide-up dark:border-white/10 dark:bg-slate-900/60 lg:block">
        <div class="overflow-x-auto">
          <table class="w-full">
            <thead class="bg-slate-50/50 dark:bg-white/5">
              <tr>
                <th class="px-3 py-3 xl:px-4 text-left text-xs font-medium uppercase tracking-wider text-slate-500 dark:text-slate-400">Invoice</th>
                <th class="px-3 py-3 xl:px-4 text-left text-xs font-medium uppercase tracking-wider text-slate-500 dark:text-slate-400">Customer</th>
                <th class="px-3 py-3 xl:px-4 text-left text-xs font-medium uppercase tracking-wider text-slate-500 dark:text-slate-400">Amount</th>
                <th class="px-3 py-3 xl:px-4 text-left text-xs font-medium uppercase tracking-wider text-slate-500 dark:text-slate-400 hidden xl:table-cell">Date</th>
                <th class="px-3 py-3 xl:px-4 text-left text-xs font-medium uppercase tracking-wider text-slate-500 dark:text-slate-400 hidden 2xl:table-cell">Xero status</th>
                <th class="px-3 py-3 xl:px-4 text-left text-xs font-medium uppercase tracking-wider text-slate-500 dark:text-slate-400">Fiscal</th>
                <th class="px-3 py-3 xl:px-4 text-right text-xs font-medium uppercase tracking-wider text-slate-500 dark:text-slate-400">Actions</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-200/30 dark:divide-white/5">
              <tr *ngFor="let d of rows(); let i = index"
                  class="cursor-pointer transition-colors duration-200 hover:bg-slate-50/70 dark:hover:bg-white/5 table-row-fade-in"
                  [style.animation-delay]="(i < 10 ? i * 0.03 : 0.3) + 's'"
                  (click)="openDetail(d, $event)">
                <td class="px-3 py-3 xl:px-4">
                  <div class="text-sm font-medium text-slate-900 dark:text-white">{{ d.invoiceNumber || 'No number' }}</div>
                  <div *ngIf="d.reference" class="max-w-[10rem] truncate text-xs text-slate-500 dark:text-slate-400 2xl:max-w-[14rem]" [title]="d.reference">Ref: {{ d.reference }}</div>
                  <span class="mt-1 inline-flex rounded-full px-2 py-0.5 text-[10px] font-semibold 2xl:hidden" [ngClass]="statusClasses(d.status)">{{ statusLabel(d.status) }}</span>
                </td>
                <td class="px-3 py-3 xl:px-4">
                  <div class="max-w-[9rem] truncate text-sm font-medium text-slate-900 dark:text-white xl:max-w-[12rem] 2xl:max-w-[16rem]" [title]="d.contact?.name || ''">{{ d.contact?.name || '—' }}</div>
                  <div class="mt-0.5 text-xs text-slate-500 dark:text-slate-400 xl:hidden">{{ date(d.dateString) }}</div>
                </td>
                <td class="whitespace-nowrap px-3 py-3 xl:px-4">
                  <div class="text-sm font-semibold tabular-nums text-slate-900 dark:text-white">{{ d.currencyCode || '' }} {{ d.total ?? 0 | number:'1.2-2' }}</div>
                  <div *ngIf="d.totalTax" class="text-xs text-primary-700 dark:text-brand-400">Tax: {{ d.totalTax | number:'1.2-2' }}</div>
                  <div *ngIf="d.amountDue && d.amountDue > 0 && d.amountDue !== d.total" class="text-xs text-amber-600 dark:text-amber-400">Due: {{ d.amountDue | number:'1.2-2' }}</div>
                </td>
                <td class="hidden whitespace-nowrap px-3 py-3 xl:table-cell xl:px-4">
                  <div class="text-sm text-slate-900 dark:text-white">{{ date(d.dateString) }}</div>
                  <div *ngIf="d.dueDateString" class="text-xs text-slate-500 dark:text-slate-400">Due: {{ date(d.dueDateString) }}</div>
                </td>
                <td class="hidden whitespace-nowrap px-3 py-3 2xl:table-cell xl:px-4">
                  <span class="inline-flex rounded-full px-2.5 py-0.5 text-[11px] font-semibold" [ngClass]="statusClasses(d.status)">{{ statusLabel(d.status) }}</span>
                </td>
                <td class="whitespace-nowrap px-3 py-3 xl:px-4">
                  <app-fiscal-pill [fiscalized]="isFiscalized(d)" [failureInfo]="failures()[d.invoiceID]"></app-fiscal-pill>
                </td>
                <td class="whitespace-nowrap px-3 py-3 xl:px-4 text-right" (click)="$event.stopPropagation()">
                  <button *ngIf="approved(d) && !isFiscalized(d)" type="button" (click)="fiscalize(d)" [disabled]="!!busyId()"
                          class="inline-flex items-center gap-1.5 rounded-lg bg-gradient-to-r from-primary-500 to-primary-600 px-3 py-1.5 text-xs font-semibold text-white shadow-md shadow-primary-500/20 transition-all duration-200 hover:from-primary-600 hover:to-primary-700 hover:scale-[1.04] active:scale-[0.96] disabled:opacity-50 disabled:hover:scale-100">
                    <svg *ngIf="busyId() === d.invoiceID" class="h-3 w-3 animate-spin" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
                    {{ busyId() === d.invoiceID ? 'Submitting…' : 'Fiscalize' }}
                  </button>
                  <a *ngIf="!approved(d) || isFiscalized(d)" [routerLink]="['/invoices', d.invoiceID]"
                     class="inline-flex items-center rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-xs font-semibold text-slate-700 hover:bg-slate-50 dark:border-white/10 dark:bg-slate-900 dark:text-slate-200 dark:hover:bg-white/5">View</a>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Mobile cards -->
      <div *ngIf="rows().length > 0" class="mb-4 space-y-4 lg:hidden">
        <a *ngFor="let d of rows(); let i = index" [routerLink]="['/invoices', d.invoiceID]"
           class="card-hover mobile-card-slide-up block rounded-2xl border border-white/40 bg-white/70 p-4 shadow-sm backdrop-blur-sm dark:border-white/10 dark:bg-slate-900/60"
           [style.animation-delay]="(i < 10 ? i * 0.03 : 0.3) + 's'">
          <div class="mb-3 flex items-start justify-between gap-2">
            <div class="min-w-0">
              <h3 class="truncate font-semibold text-slate-900 dark:text-white">{{ d.invoiceNumber || 'No number' }}</h3>
              <span class="mt-1 inline-flex rounded-full px-2 py-0.5 text-[10px] font-semibold" [ngClass]="statusClasses(d.status)">{{ statusLabel(d.status) }}</span>
            </div>
            <app-fiscal-pill [fiscalized]="isFiscalized(d)" [failureInfo]="failures()[d.invoiceID]"></app-fiscal-pill>
          </div>
          <p class="mb-3 truncate font-medium text-slate-900 dark:text-white">{{ d.contact?.name || '—' }}</p>
          <div class="flex items-center justify-between">
            <div>
              <p class="text-lg font-bold tabular-nums text-slate-900 dark:text-white">{{ d.currencyCode || '' }} {{ d.total ?? 0 | number:'1.2-2' }}</p>
              <p *ngIf="d.totalTax" class="text-xs text-primary-700 dark:text-brand-400">Tax: {{ d.totalTax | number:'1.2-2' }}</p>
            </div>
            <div class="text-right">
              <p class="text-sm text-slate-900 dark:text-white">{{ date(d.dateString) }}</p>
              <p *ngIf="d.dueDateString" class="text-xs text-slate-500">Due: {{ date(d.dueDateString) }}</p>
            </div>
          </div>
        </a>
      </div>
      </div>

      <app-pagination *ngIf="list.total() > 0" class="mb-2 block"
                      [page]="list.page()" [totalPages]="list.totalPages()" [hasMore]="list.page() < list.totalPages()" [loading]="list.loading()"
                      [total]="list.total()" [pageSize]="list.pageSize()" [pageCount]="rows().length" [pageSizes]="pageSizes" itemLabel="invoices"
                      (pageChange)="list.goTo($event)" (pageSizeChange)="list.setPageSize($event)"></app-pagination>
    </ng-container>
  `
})
export class Invoices implements OnInit {
  private documents = inject(InvoicesService);
  private fiscalizationService = inject(FiscalizationService);
  private fiscalCache = inject(FiscalCacheService);
  private router = inject(Router);
  private toast = inject(ToastService);

  readonly rows = signal<Invoice[]>([]);
  /** Set when Xero refused the list (the connection needs reconnecting, or Xero's limits were hit). */
  readonly xeroProblem = signal<string | null>(null);
  readonly list = new DocumentList<Invoice>('/invoices', (q) => this.documents.page(q), (items) => this.onLoaded(items),
    (err) => {
      const message = extractErrorMessage(err, 'Failed to load invoices.');
      if (isXeroAttention(err) || err.status === 429) this.xeroProblem.set(message);
      else this.toast.error(message);
    });
  /** First load only; later pages keep the rows on screen (dimmed) while the next page arrives. */
  readonly loading = computed(() => this.list.loading() && !this.list.loaded() && !this.list.failed());
  /** Latest failed attempt per document id, for the "Failed" pill. */
  readonly failures = signal<Record<string, FiscalFailure>>({});
  readonly busyId = signal<string | null>(null);
  readonly pageSizes = PAGE_SIZES;
  readonly statusFilters = STATUS_FILTERS;

  readonly statusLabel = statusLabel;
  readonly statusClasses = statusClasses;
  readonly date = calendarDate;

  ngOnInit(): void { this.list.init(); }

  onSearch(text: string): void { this.list.setSearch(text); }
  reload(): void { this.xeroProblem.set(null); this.list.reload(true); }

  approved(d: Invoice): boolean { return isApproved(d.status); }
  isFiscalized(d: Invoice): boolean { return this.fiscalCache.has(d.invoiceID); }

  openDetail(d: Invoice, ev: MouseEvent): void {
    if ((ev.target as HTMLElement).closest('button, input, a, label')) return;
    this.router.navigate(['/invoices', d.invoiceID]);
  }

  /** Failures for the documents on screen. A failed lookup just leaves the pills as they were. */
  private loadFailures(ids: string[]): void {
    this.fiscalizationService.failures('invoices', ids).subscribe({
      next: (list) => {
        // Paged on meanwhile: these are for rows no longer on screen.
        if (this.list.items().map((d) => d.invoiceID).join() !== ids.join()) return;
        this.failures.set(byDocumentId(list));
      },
      error: () => { /* pills fall back to Fiscalized / Not Fiscalized */ }
    });
  }

  /** After a failed manual attempt: show its pill straight away. */
  private refreshFailure(id: string): void {
    this.fiscalizationService.failures('invoices', [id]).subscribe({
      next: (list) => this.failures.update((m) => {
        const next = { ...m };
        delete next[id];
        return { ...next, ...byDocumentId(list) };
      }),
      error: () => {}
    });
  }

  /** A page arrived: show it, then ask the API which of its documents are fiscalised or failed. */
  private onLoaded(res: Invoice[]): void {
    this.xeroProblem.set(null);
    this.rows.set(res ?? []);
    const ids = (res ?? []).map((d) => d.invoiceID);
    // Which documents are fiscalized is known only to the API: forget what an earlier visit cached, then ask.
    ids.forEach((id) => this.fiscalCache.clear(id));
    this.loadFailures(ids);
    this.fiscalizationService.records('invoices', ids).subscribe({
      next: (recs) => {
        if (this.list.items() !== res) return;
        recs.forEach((r) => this.fiscalCache.cache(r.documentId, r));
        this.rows.set([...(res ?? [])]);
      },
      error: () => { /* pills stay "Not fiscalized" until the next load */ }
    });
  }

  fiscalize(d: Invoice): void {
    this.busyId.set(d.invoiceID);
    const docNo = d.invoiceNumber || 'without a number';
    this.fiscalizationService.fiscalizeInvoice(d.invoiceID).subscribe({
      next: (res) => {
        this.busyId.set(null);
        this.fiscalCache.cache(d.invoiceID, res);
        this.failures.update((m) => { const next = { ...m }; delete next[d.invoiceID]; return next; });
        if (res.writebackWarning) {
          this.toast.warning(`Invoice ${docNo} fiscalized, but the result could not be written to Xero: ${res.writebackWarning}`);
        } else {
          this.toast.success(res.alreadyFiscalized ? `Invoice ${docNo} was already fiscalized. Fiscal data updated from the platform.` : `Invoice ${docNo} fiscalized successfully.`);
        }
        this.rows.set([...this.rows()]);
      },
      error: (err) => {
        this.busyId.set(null);
        this.refreshFailure(d.invoiceID);
        const message = extractErrorMessage(err, 'Fiscalization failed. Please try again.');
        this.toast.error(message);
      }
    });
  }
}
