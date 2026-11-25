import { Component, Input, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { CustomerTaxDetails, TaxDetailsService } from '../../core/services/xero/tax-details.service';
import { extractErrorMessage } from '../../core/utils/http-error';
import { BUYER_SECTIONS, BuyerKey } from '../../pages/customer-detail/buyer-fields';

/** Placeholders the server sends in Lenient mode when a TIN customer lacks a detail (BuyerCompliance.Lenient*). */
const LENIENT: Partial<Record<BuyerKey, string>> = {
  phone: '0770000000', email: 'customer@email.com', houseNo: '01', street: 'Harare', city: 'Harare', province: 'Harare'
};
const MAX_TRADE_NAME = 50;
const OPEN_KEY = 'fqb.buyerDetailsOpen';

interface Row {
  key: BuyerKey;
  label: string;
  /** The value fiscalising will send, or null when there is none. */
  value: string | null;
  mono?: boolean;
  /** Typed in Fiscalize (wins over Xero). */
  fromFiscalize: boolean;
  /** Shown under the value, e.g. "Same as registered name". */
  note?: string;
  /** ZIMRA needs it and it is missing. */
  missing: boolean;
}

interface Group { key: string; title: string; icon: string; rows: Row[] }

/**
 * The buyer a document will be fiscalised with, resolved exactly as the fiscaliser does (Fiscalize values win over
 * Xero; a TIN makes it B2B, none makes it a cash sale), so the user can check TIN, VAT number, contacts and
 * address before fiscalising, and jump to the customer to fix them. Laid out like the document's own panels.
 */
@Component({
  selector: 'app-buyer-summary',
  imports: [CommonModule, RouterLink],
  template: `
    <section class="overflow-hidden rounded-2xl border border-white/40 bg-white/70 shadow-sm backdrop-blur-md dark:border-white/10 dark:bg-slate-900/60"
             aria-labelledby="buyer-summary-title" [attr.aria-busy]="loading()">
      <!-- Header -->
      <header class="flex flex-col gap-4 border-slate-200/50 px-5 py-5 sm:flex-row sm:items-start sm:justify-between sm:px-6 dark:border-white/5" [class.border-b]="showBody()">
        <div class="flex min-w-0 items-start gap-3.5">
          <div class="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-xl ring-1 ring-inset" [ngClass]="tone().tile">
            <svg class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
              <path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75"/>
            </svg>
          </div>
          <div class="min-w-0">
            <div class="flex flex-wrap items-center gap-x-2.5 gap-y-1">
              <h2 id="buyer-summary-title" class="text-lg font-semibold tracking-tight text-slate-900 dark:text-white">Buyer details</h2>
              @if (customer()) {
                <span class="inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-semibold ring-1 ring-inset" [ngClass]="tone().badge">
                  <span class="h-1.5 w-1.5 rounded-full" [ngClass]="tone().dot"></span>{{ tone().label }}
                </span>
              }
            </div>
            @if (showBody() || !customer()) {
              <p class="mt-1 max-w-2xl text-sm leading-relaxed text-slate-500 dark:text-slate-400">{{ loading() ? 'Loading the buyer this document is fiscalised with…' : intro() }}</p>
            } @else {
              <!-- Collapsed: the facts that matter at a glance -->
              <p class="mt-1 flex flex-wrap items-center gap-x-2 gap-y-0.5 text-sm text-slate-600 dark:text-slate-300">
                @for (f of glance(); track f.label; let last = $last) {
                  <span class="min-w-0 [overflow-wrap:anywhere]">@if (f.label) {<span class="text-slate-400 dark:text-slate-500">{{ f.label }} </span>}<span [class.font-mono]="f.mono" [class.font-medium]="!f.muted" [class.text-slate-400]="f.muted">{{ f.value }}</span></span>
                  @if (!last) { <span class="text-slate-300 dark:text-slate-600" aria-hidden="true">·</span> }
                }
              </p>
            }
          </div>
        </div>
        @if (custId()) {
          <div class="flex flex-shrink-0 items-center gap-2 self-start">
          @if (customer()) {
            <button type="button" (click)="toggle()" [attr.aria-expanded]="showBody()" aria-controls="buyer-summary-body"
                    class="inline-flex items-center gap-1.5 rounded-xl px-3 py-2 text-sm font-medium text-slate-600 transition hover:bg-slate-100 hover:text-slate-900 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary-500 dark:text-slate-300 dark:hover:bg-white/5 dark:hover:text-white">
              {{ showBody() ? 'Hide details' : 'Show details' }}
              <svg class="h-4 w-4 transition-transform duration-200" [class.rotate-180]="showBody()" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m6 9 6 6 6-6"/></svg>
            </button>
          }
          <a [routerLink]="['/customers', custId()]" [queryParams]="{ returnTo: returnTo() }"
             class="inline-flex flex-shrink-0 items-center justify-center gap-2 self-start rounded-xl border border-slate-200 bg-white px-3.5 py-2 text-sm font-medium text-slate-700 shadow-sm transition hover:border-slate-300 hover:bg-slate-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary-500 focus-visible:ring-offset-2 dark:border-white/10 dark:bg-slate-800 dark:text-slate-200 dark:hover:bg-slate-700 dark:focus-visible:ring-offset-slate-900">
            <svg class="h-4 w-4 text-primary-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 20h9"/><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>
            <span class="hidden sm:inline">Edit buyer details</span><span class="sm:hidden">Edit</span>
          </a>
          </div>
        }
      </header>

      @if (showBody()) {
      <div id="buyer-summary-body" class="px-5 py-5 sm:px-6 fade-in">
        @if (loading()) {
          <div class="grid grid-cols-1 gap-4 lg:grid-cols-3" aria-label="Loading buyer details">
            @for (g of [[1, 2, 3, 4], [1, 2], [1, 2, 3, 4]]; track $index) {
              <div class="rounded-xl border border-slate-200/60 p-4 dark:border-white/5">
                <div class="h-3 w-24 animate-pulse rounded bg-slate-200 dark:bg-white/10"></div>
                @for (r of g; track r) {
                  <div class="mt-4 flex justify-between gap-4"><div class="h-3.5 w-20 animate-pulse rounded bg-slate-200/80 dark:bg-white/10"></div><div class="h-3.5 w-28 animate-pulse rounded bg-slate-200 dark:bg-white/10"></div></div>
                }
              </div>
            }
          </div>
        } @else if (error()) {
          <div class="flex flex-wrap items-center gap-3 rounded-xl border border-rose-200/70 bg-rose-50/70 px-4 py-3 text-sm text-rose-700 dark:border-rose-500/30 dark:bg-rose-500/10 dark:text-rose-300" role="alert">
            <span class="min-w-0 flex-1">{{ error() }}</span>
            <button type="button" (click)="load()" class="font-semibold text-rose-700 underline-offset-2 hover:underline dark:text-rose-200">Try again</button>
          </div>
        } @else if (customer(); as c) {
          <!-- What needs attention -->
          @if (c.problems.length) {
            <div class="mb-5 flex gap-3 rounded-xl border border-rose-200/70 bg-rose-50/70 px-4 py-3 dark:border-rose-500/30 dark:bg-rose-500/10" role="alert">
              <svg class="mt-0.5 h-4 w-4 flex-shrink-0 text-rose-600 dark:text-rose-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"/><path d="M15 9l-6 6M9 9l6 6"/></svg>
              <div class="min-w-0 text-sm">
                <p class="font-semibold text-rose-800 dark:text-rose-200">ZIMRA will reject this buyer</p>
                <ul class="mt-1 space-y-0.5 text-rose-700/90 dark:text-rose-300/90">@for (p of c.problems; track p) { <li>{{ p }}</li> }</ul>
              </div>
            </div>
          } @else if (c.missing.length) {
            <div class="mb-5 flex gap-3 rounded-xl border border-amber-200/70 bg-amber-50/70 px-4 py-3 dark:border-amber-500/30 dark:bg-amber-500/10">
              <svg class="mt-0.5 h-4 w-4 flex-shrink-0 text-amber-600 dark:text-amber-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><path d="M12 9v4M12 17h.01"/></svg>
              <div class="min-w-0 text-sm">
                <p class="font-semibold text-amber-900 dark:text-amber-200">{{ c.missing.length }} {{ c.missing.length === 1 ? 'detail' : 'details' }} missing: {{ c.missing.join(', ') }}</p>
                <p class="mt-0.5 text-amber-800/90 dark:text-amber-200/80">{{ lenient()
                  ? 'Buyer validation is Lenient, so the placeholders shown below are sent instead. Add the real details to send them.'
                  : 'Buyer validation is Strict, so fiscalising stops until these are added.' }}</p>
              </div>
            </div>
          }

          <!-- Identity · Contact · Address -->
          <div class="grid grid-cols-1 gap-4 lg:grid-cols-3" [class.opacity-60]="cashSale()">
            @for (g of groups(); track g.key) {
              <div class="min-w-0 rounded-xl border border-slate-200/60 bg-white/50 p-4 dark:border-white/5 dark:bg-white/[0.02]">
                <h3 class="flex items-center gap-2 text-[11px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">
                  <svg class="h-3.5 w-3.5 text-slate-400 dark:text-slate-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path [attr.d]="g.icon"/></svg>
                  {{ g.title }}
                </h3>
                <dl class="mt-3 divide-y divide-slate-200/50 dark:divide-white/5">
                  @for (r of g.rows; track r.key) {
                    <div class="flex items-start justify-between gap-4 py-2.5 first:pt-0 last:pb-0">
                      <dt class="flex-shrink-0 pt-px text-sm text-slate-500 dark:text-slate-400">{{ r.label }}</dt>
                      <dd class="min-w-0 text-right text-sm">
                        @if (r.missing) {
                          <span class="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-[11px] font-semibold text-amber-700 dark:bg-amber-500/15 dark:text-amber-300">
                            <span class="h-1.5 w-1.5 rounded-full bg-amber-500"></span>Missing
                          </span>
                          @if (lenient() && placeholder(r.key)) {
                            <span class="mt-1 block text-xs text-slate-500 dark:text-slate-400">Sends <span class="font-mono text-slate-700 dark:text-slate-300">{{ placeholder(r.key) }}</span></span>
                          }
                        } @else if (r.value) {
                          <span class="inline-flex max-w-full items-center justify-end gap-1.5">
                            <span class="min-w-0 font-medium text-slate-900 [overflow-wrap:anywhere] dark:text-white" [class.font-mono]="r.mono" [class.tabular-nums]="r.mono">{{ r.value }}</span>
                            @if (r.fromFiscalize) {
                              <span class="h-1.5 w-1.5 flex-shrink-0 rounded-full bg-primary-500" title="Saved in Fiscalize, used instead of Xero"></span>
                              <span class="sr-only">(saved in Fiscalize)</span>
                            }
                          </span>
                        } @else {
                          <span class="text-slate-400 dark:text-slate-500">—</span>
                        }
                        @if (r.note) { <span class="mt-0.5 block text-xs text-slate-500 dark:text-slate-400">{{ r.note }}</span> }
                      </dd>
                    </div>
                  }
                </dl>
              </div>
            }
          </div>

          <!-- Footer: legend and, once fiscalised, what the card shows -->
          <div class="mt-4 flex flex-col gap-2 text-xs text-slate-500 sm:flex-row sm:items-start sm:justify-between sm:gap-6 dark:text-slate-400">
            <p class="flex items-center gap-1.5">
              <span class="h-1.5 w-1.5 flex-shrink-0 rounded-full bg-primary-500" aria-hidden="true"></span>
              Saved in Fiscalize. Everything else is read from the Xero contact.
            </p>
            @if (fiscalized) {
              <p class="flex max-w-xl gap-1.5 sm:text-right">
                <svg class="mt-px h-3.5 w-3.5 flex-shrink-0 sm:order-last" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4M12 8h.01"/></svg>
                <span>Shows the customer's details now. If they changed after this {{ documentLabel }} was fiscalised, refiscalize to send the new ones.</span>
              </p>
            }
          </div>
        }
      </div>
      }
    </section>
  `
})
export class BuyerSummary {
  private service = inject(TaxDetailsService);
  private router = inject(Router);

  /** The document's Xero contact id; none means a walk-in cash sale. */
  @Input() set customerId(v: string | null | undefined) {
    const id = v ?? null;
    if (id === this.id()) return;
    this.id.set(id);
    this.load();
  }
  /** "invoice" or "credit note", used in sentences. */
  @Input() documentLabel = 'document';
  /** Already fiscalised: the values shown may differ from what ZIMRA received. */
  @Input() fiscalized = false;
  /**
   * Anything that changes after a fiscalise, refiscalise or fiscal-details update (the page's fiscal result). Those
   * can save corrected buyer details to the customer, so the card reloads to show them.
   */
  @Input() set refreshOn(v: unknown) {
    // Only a change from one result to another: the page's first load (none -> stored record) changes nothing for the buyer.
    if (this.lastRefresh != null && v != null && v !== this.lastRefresh && this.id()) this.load(true);
    this.lastRefresh = v;
  }
  private lastRefresh: unknown = null;

  private readonly id = signal<string | null>(null);
  readonly custId = this.id.asReadonly();
  readonly customer = signal<CustomerTaxDetails | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  private sub?: Subscription;

  /** The user's own open/closed choice on this page; null until they toggle. */
  private readonly open = signal<boolean | null>(null);
  private readonly storedOpen = (() => { try { return localStorage.getItem(OPEN_KEY) === '1'; } catch { return false; } })();
  /** Something to fix: show the details without being asked. */
  readonly needsAttention = computed(() => ['MissingDetails', 'Invalid'].includes(this.customer()?.complianceStatus ?? ''));
  /** Details open: always for a load error; otherwise the user's choice, else open when something needs attention. */
  readonly showBody = computed(() => {
    if (!this.custId()) return false;
    if (this.error()) return true;
    return this.open() ?? (this.needsAttention() || this.storedOpen);
  });

  toggle(): void {
    const next = !this.showBody();
    this.open.set(next);
    try { localStorage.setItem(OPEN_KEY, next ? '1' : '0'); } catch { /* private mode */ }
  }

  /** Collapsed summary: who, TIN and VAT number (or why nothing is sent). */
  readonly glance = computed(() => {
    const c = this.customer();
    if (!c) return [];
    const name = c.registeredName?.trim() || c.displayName || '—';
    if (c.complianceStatus === 'NoTin') return [{ label: '', value: name, mono: false, muted: false }, { label: '', value: 'No TIN, so no buyer details are sent', mono: false, muted: true }];
    return [
      { label: '', value: name, mono: false, muted: false },
      { label: 'TIN', value: c.tin?.trim() || '—', mono: true, muted: false },
      { label: 'VAT', value: c.vatNumber?.trim() || 'not registered', mono: !!c.vatNumber?.trim(), muted: !c.vatNumber?.trim() },
    ];
  });

  readonly lenient = computed(() => this.customer()?.validationMode === 'Lenient');
  readonly cashSale = computed(() => this.customer()?.complianceStatus === 'NoTin');

  readonly tone = computed(() => {
    switch (this.customer()?.complianceStatus) {
      case 'Compliant': return { label: 'Ready for B2B', tile: 'bg-emerald-50 text-emerald-600 ring-emerald-500/15 dark:bg-emerald-500/10 dark:text-emerald-400',
        badge: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20 dark:bg-emerald-500/15 dark:text-emerald-300', dot: 'bg-emerald-500' };
      case 'MissingDetails': return { label: 'Missing details', tile: 'bg-amber-50 text-amber-600 ring-amber-500/15 dark:bg-amber-500/10 dark:text-amber-400',
        badge: 'bg-amber-50 text-amber-700 ring-amber-600/20 dark:bg-amber-500/15 dark:text-amber-300', dot: 'bg-amber-500' };
      case 'Invalid': return { label: 'Invalid', tile: 'bg-rose-50 text-rose-600 ring-rose-500/15 dark:bg-rose-500/10 dark:text-rose-400',
        badge: 'bg-rose-50 text-rose-700 ring-rose-600/20 dark:bg-rose-500/15 dark:text-rose-300', dot: 'bg-rose-500' };
      case 'NoTin': return { label: 'Cash sale', tile: 'bg-slate-100 text-slate-500 ring-slate-500/10 dark:bg-white/5 dark:text-slate-400',
        badge: 'bg-slate-100 text-slate-600 ring-slate-500/20 dark:bg-white/10 dark:text-slate-300', dot: 'bg-slate-400' };
      default: return { label: '', tile: 'bg-brand-50 text-primary-600 ring-primary-500/15 dark:bg-primary-500/10 dark:text-brand-400', badge: '', dot: '' };
    }
  });

  readonly intro = computed(() => {
    if (!this.custId()) return `There is no Xero contact on this ${this.documentLabel}, so it is fiscalised as a cash sale and no buyer details are sent.`;
    const c = this.customer();
    if (c?.complianceStatus === 'NoTin')
      return 'This customer has no TIN, so the document is fiscalised as a cash sale and these details are not sent. Add a TIN to make it a B2B sale.';
    return 'What is sent to ZIMRA as the buyer when this document is fiscalised. Check the TIN and VAT number before you fiscalise.';
  });

  readonly groups = computed<Group[]>(() => {
    const c = this.customer();
    if (!c) return [];
    const tin = c.complianceStatus !== 'NoTin';
    const has = (m: string) => tin && c.missing.some((x) => x.toLowerCase().includes(m));
    const typed = (k: BuyerKey) => !!(c.saved?.[k] ?? '').toString().trim();
    const registered = c.registeredName?.trim() || null;
    const registeredSent = registered || c.displayName || null;
    const trade = c.tradeName?.trim() || null;
    const tradeSent = (trade || registeredSent || '').slice(0, MAX_TRADE_NAME) || null;
    const rows: Record<BuyerKey, Row> = {
      registeredName: { key: 'registeredName', label: 'Registered name', value: registeredSent, fromFiscalize: !!registered && typed('registeredName'),
        note: !registered && registeredSent ? 'Display name; no company name set' : undefined, missing: has('registered') },
      tradeName: { key: 'tradeName', label: 'Trade name', value: tradeSent, fromFiscalize: !!trade && typed('tradeName'),
        note: !trade ? 'Same as registered name' : (trade.length > MAX_TRADE_NAME ? `Shortened to ${MAX_TRADE_NAME} characters` : undefined), missing: false },
      tin: { key: 'tin', label: 'TIN', value: c.tin?.trim() || null, mono: true, fromFiscalize: typed('tin'), missing: false,
        note: !c.tin ? 'None, so cash sale' : undefined },
      vatNumber: { key: 'vatNumber', label: 'VAT number', value: c.vatNumber?.trim() || null, mono: true, fromFiscalize: typed('vatNumber'), missing: false,
        note: !c.vatNumber && tin ? 'Not VAT registered' : undefined },
      phone: { key: 'phone', label: 'Phone', value: c.phone || null, mono: true, fromFiscalize: typed('phone'), missing: has('phone'),
        note: c.phoneSource && c.phoneSource !== 'Phone' && c.phoneSource !== 'Saved in Fiscalize' ? `From ${c.phoneSource}` : undefined },
      email: { key: 'email', label: 'Email', value: c.email || null, fromFiscalize: typed('email'), missing: has('email') },
      houseNo: { key: 'houseNo', label: 'House no.', value: c.houseNo || null, fromFiscalize: typed('houseNo'), missing: has('house') },
      street: { key: 'street', label: 'Street', value: c.street || null, fromFiscalize: typed('street'), missing: has('street') },
      city: { key: 'city', label: 'City / town', value: c.city || null, fromFiscalize: typed('city'), missing: has('city') },
      province: { key: 'province', label: 'Province', value: c.province || null, fromFiscalize: typed('province'), missing: has('province') },
    };
    // Same sections and icons as the customer page, so the two read alike.
    return BUYER_SECTIONS.map((s) => ({ key: s.key, title: s.key === 'business' ? 'Identity' : s.title, icon: s.icon, rows: s.fields.map((f) => rows[f.key]) }));
  });

  placeholder(k: BuyerKey): string | null { return k === 'registeredName' ? (this.customer()?.displayName ?? null) : (LENIENT[k] ?? null); }
  returnTo(): string { return this.router.url.split('?')[0]; }

  /** Fetch the customer. A quiet reload keeps the current values on screen instead of flashing the skeleton. */
  load(quiet = false): void {
    this.sub?.unsubscribe();
    this.error.set(null);
    const id = this.id();
    if (!id) { this.customer.set(null); this.loading.set(false); return; }
    if (!quiet || !this.customer()) { this.customer.set(null); this.loading.set(true); }
    this.sub = this.service.getCustomer(id).subscribe({
      next: (c) => { this.customer.set(c); this.loading.set(false); },
      error: (err) => {
        this.loading.set(false);
        this.customer.set(null);
        this.error.set(err?.status === 404 ? 'This customer no longer exists in Xero.' : extractErrorMessage(err, 'Could not load the buyer details.'));
      }
    });
  }
}
