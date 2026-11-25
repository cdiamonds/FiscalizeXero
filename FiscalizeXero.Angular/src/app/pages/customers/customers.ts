import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subject, switchMap, catchError, EMPTY } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  TaxDetailsService, CustomerTaxDetails, CustomerComplianceCounts, CustomerComplianceStatus
} from '../../core/services/xero/tax-details.service';
import { extractErrorMessage } from '../../core/utils/http-error';
import { PageHeader } from '../../components/page-header/page-header';
import { SearchBar } from '../../components/search-bar/search-bar';
import { Pagination } from '../../components/pagination/pagination';
import { customerInitials } from '../customer-detail/buyer-fields';

type StatusFilter = CustomerComplianceStatus | 'Attention' | '';

const PAGE_SIZE = 25;
const STATUSES: StatusFilter[] = ['', 'Attention', 'Compliant', 'MissingDetails', 'Invalid', 'NoTin'];

/**
 * Every Xero contact with its ZIMRA buyer compliance, worked out on the server by the same code that
 * fiscalizes, so a "Compliant" badge here means fiscalizing will accept it. A row opens the customer's page, where
 * every buyer detail can be typed; the current filter travels along so "Save & next" walks the same list.
 */
@Component({
  selector: 'app-customers',
  imports: [CommonModule, RouterLink, PageHeader, SearchBar, Pagination],
  template: `
    <app-page-header title="Customers" subtitle="ZIMRA buyer details for every Xero contact. Open a customer to fill in anything missing right here, with no need to go into Xero." />

    <!-- Validation mode banner -->
    <div class="mb-4 flex flex-col gap-2 rounded-2xl border px-4 py-3 text-xs leading-relaxed fade-in sm:flex-row sm:items-center sm:justify-between"
         [ngClass]="mode() === 'Strict'
           ? 'border-brand-200/60 bg-brand-50/60 text-brand-800 dark:border-primary-500/20 dark:bg-primary-500/10 dark:text-brand-200'
           : 'border-amber-200/70 bg-amber-50/70 text-amber-800 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-200'">
      <div class="flex items-start gap-2.5">
        <svg class="mt-0.5 h-4 w-4 flex-shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>
        <span>
          <span class="font-semibold">Buyer validation: {{ mode() }}.</span>
          @if (mode() === 'Strict') {
            Customers with a TIN must have phone, email, house number, street, city and province, or their documents fail with a list of what to add.
          } @else {
            Missing details on TIN customers are filled with placeholders. Invalid TIN or VAT numbers still stop fiscalization.
          }
        </span>
      </div>
      <a routerLink="/fiscalization-settings" class="inline-flex flex-shrink-0 items-center gap-1 font-semibold underline-offset-2 hover:underline">Change</a>
    </div>

    <!-- Summary cards (click to filter) -->
    <div class="mb-4 grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
      @for (card of cards(); track card.status) {
        <button type="button" (click)="setStatus(card.status)" [attr.aria-pressed]="status() === card.status"
                class="group rounded-2xl border bg-white/70 p-3.5 text-left shadow-sm backdrop-blur-sm transition-all duration-200 hover:-translate-y-0.5 hover:shadow-md focus:outline-none focus-visible:ring-2 focus-visible:ring-primary-500 dark:bg-slate-900/60"
                [ngClass]="status() === card.status ? card.activeRing : 'border-white/40 dark:border-white/10'">
          <div class="flex items-center justify-between">
            <span class="text-[11px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">{{ card.label }}</span>
            <span class="h-2 w-2 rounded-full" [ngClass]="card.dot"></span>
          </div>
          <div class="mt-1.5 text-2xl font-bold tabular-nums text-slate-900 dark:text-white">
            @if (countsLoaded()) { {{ card.count }} } @else { <span class="inline-block h-6 w-10 animate-pulse rounded bg-slate-200 dark:bg-white/10"></span> }
          </div>
          <div class="mt-0.5 truncate text-[11px] text-slate-500 dark:text-slate-400">{{ card.hint }}</div>
        </button>
      }
    </div>

    <!-- Search + filter + refresh -->
    <div class="mb-4 rounded-2xl border border-white/40 bg-white/70 p-3 shadow-sm backdrop-blur-sm slide-down dark:border-white/10 dark:bg-slate-900/60">
      <div class="flex flex-col gap-2 lg:flex-row lg:items-center lg:gap-3">
        <app-search-bar class="block min-w-0 flex-1" placeholder="Search name, email, phone, TIN or VAT…" (valueChange)="onSearch($event)"></app-search-bar>
        <div class="flex items-center gap-2">
          <div class="grid flex-1 grid-cols-2 gap-1 rounded-xl border border-slate-200 bg-slate-50 p-1 sm:flex lg:flex-none dark:border-white/10 dark:bg-white/5" role="tablist" aria-label="Filter by compliance">
            @for (t of tabs; track t.value) {
              <button type="button" role="tab" [attr.aria-selected]="status() === t.value" (click)="setStatus(t.value)"
                      class="whitespace-nowrap rounded-lg px-3 py-1.5 text-xs font-semibold transition"
                      [ngClass]="status() === t.value ? 'bg-white text-primary-700 shadow-sm ring-1 ring-slate-200 dark:bg-slate-800 dark:text-brand-300 dark:ring-white/10' : 'text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-slate-200'">
                {{ t.label }}
              </button>
            }
          </div>
          <button type="button" (click)="reload()" [disabled]="loading()"
                  class="inline-flex flex-shrink-0 items-center justify-center rounded-xl border border-slate-200 bg-white/70 px-3 py-2.5 text-slate-600 transition hover:bg-white disabled:cursor-not-allowed disabled:opacity-50 dark:border-white/10 dark:bg-slate-950/60 dark:text-slate-300 dark:hover:bg-white/5"
                  title="Refresh" aria-label="Refresh customers">
            <svg class="h-5 w-5" [class.animate-spin]="loading()" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"/></svg>
          </button>
        </div>
      </div>
    </div>

    @if (truncated()) {
      <p class="mb-3 rounded-xl bg-amber-50 px-3 py-2 text-xs text-amber-800 dark:bg-amber-500/10 dark:text-amber-200">This company has more than 10,000 active customers; only the first 10,000 are counted. Search to find a specific customer.</p>
    }

    @if (!loading() && countsLoaded()) {
      <div class="mb-3 flex items-center gap-2 text-sm text-slate-500 dark:text-slate-400 fade-in">
        <span>{{ total() }} {{ total() === 1 ? 'customer' : 'customers' }}</span>
        @if (search || status()) { <span class="text-primary-600 dark:text-brand-400">Filtered</span> }
      </div>
    }

    @if (loading()) {
      <div class="space-y-2" aria-busy="true" aria-label="Loading customers">
        @for (i of [1,2,3,4,5]; track i) {
          <div class="h-16 animate-pulse rounded-2xl bg-white/60 dark:bg-white/5"></div>
        }
      </div>
    }

    @if (!loading() && error()) {
      <div class="rounded-2xl border border-rose-200 bg-rose-50 p-6 text-center text-sm text-rose-700 dark:border-rose-500/30 dark:bg-rose-500/10 dark:text-rose-300">
        {{ error() }}
        <button type="button" (click)="reload()" class="ml-2 font-semibold underline">Try again</button>
      </div>
    }

    @if (!loading() && !error()) {
      @if (rows().length === 0) {
        <div class="rounded-2xl border border-white/40 bg-white/70 p-12 text-center shadow-sm backdrop-blur-sm empty-state-fade-in dark:border-white/10 dark:bg-slate-900/60">
          <div class="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-2xl bg-gradient-to-br from-slate-100 to-slate-200 text-slate-400 dark:from-white/5 dark:to-white/10 dark:text-slate-500">
            <svg class="h-8 w-8" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0z"/></svg>
          </div>
          <h3 class="mb-2 text-lg font-medium text-slate-900 dark:text-white">{{ status() === 'Attention' ? 'Every customer is ready for ZIMRA' : 'No customers found' }}</h3>
          <p class="text-slate-600 dark:text-slate-400">{{ status() || search ? 'Try another filter or search.' : 'No active customers in this Xero organisation yet.' }}</p>
        </div>
      }

      @if (rows().length > 0) {
        <div class="mb-4 overflow-hidden rounded-2xl border border-white/40 bg-white/70 shadow-sm backdrop-blur-sm table-slide-up dark:border-white/10 dark:bg-slate-900/60">
          <!-- Column headings (desktop) -->
          <div class="hidden grid-cols-[minmax(0,1.4fr)_minmax(0,1.2fr)_minmax(0,1.3fr)_minmax(0,0.9fr)_minmax(0,1.1fr)_4rem] gap-4 bg-slate-50/50 px-4 py-3 text-xs font-medium uppercase tracking-wider text-slate-500 lg:grid dark:bg-white/5 dark:text-slate-400">
            <span>Customer</span><span>Contact</span><span>Address</span><span>TIN / VAT</span><span>ZIMRA compliance</span><span></span>
          </div>

          <ul class="divide-y divide-slate-200/40 dark:divide-white/5">
            @for (r of rows(); track r.customerId; let i = $index) {
              <li class="table-row-fade-in" [style.animation-delay]="(i * 0.03) + 's'">
                <a [routerLink]="['/customers', r.customerId]" [queryParams]="rowQuery()" [attr.aria-label]="'Open ' + r.displayName"
                   class="group grid w-full grid-cols-1 gap-3 px-4 py-3 text-left transition-colors hover:bg-slate-50/70 focus:outline-none focus-visible:bg-slate-50 focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary-500 lg:grid-cols-[minmax(0,1.4fr)_minmax(0,1.2fr)_minmax(0,1.3fr)_minmax(0,0.9fr)_minmax(0,1.1fr)_4rem] lg:items-center lg:gap-4 dark:hover:bg-white/5 dark:focus-visible:bg-white/5">
                  <!-- Customer -->
                  <div class="flex min-w-0 items-center gap-3">
                    <span class="avatar-chip h-9 w-9 flex-shrink-0">{{ initials(r.displayName) }}</span>
                    <div class="min-w-0 flex-1">
                      <div class="truncate text-sm font-medium text-slate-900 dark:text-white">{{ r.displayName }}</div>
                      <div class="truncate text-xs text-slate-500 dark:text-slate-400">
                        {{ r.registeredName && r.registeredName !== r.displayName ? r.registeredName : (r.source === 'Override' ? 'Saved in Fiscalize' : 'From Xero') }}
                      </div>
                    </div>
                    <!-- Status on mobile -->
                    <span class="flex-shrink-0 lg:hidden"><ng-container *ngTemplateOutlet="badge; context: { $implicit: r }"></ng-container></span>
                  </div>

                  <!-- Contact -->
                  <div class="min-w-0 text-xs">
                    <div class="flex items-center gap-1.5 truncate">
                      <svg class="h-3.5 w-3.5 flex-shrink-0 text-slate-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6A19.79 19.79 0 0 1 2.12 4.18 2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92z"/></svg>
                      @if (r.phone) {
                        <span class="truncate font-mono text-slate-700 dark:text-slate-200">{{ r.phone }}</span>
                        @if (r.phoneSource && r.phoneSource !== 'Phone') { <span class="rounded bg-slate-100 px-1 text-[10px] font-medium text-slate-500 dark:bg-white/10 dark:text-slate-400">{{ r.phoneSource === 'Saved in Fiscalize' ? 'Fiscalize' : r.phoneSource }}</span> }
                      } @else { <span class="text-slate-400 dark:text-slate-500">No phone</span> }
                    </div>
                    <div class="mt-0.5 flex items-center gap-1.5 truncate">
                      <svg class="h-3.5 w-3.5 flex-shrink-0 text-slate-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="2" y="4" width="20" height="16" rx="2"/><path d="m22 7-10 5L2 7"/></svg>
                      @if (r.email) { <span class="truncate text-slate-600 dark:text-slate-300">{{ r.email }}</span> } @else { <span class="text-slate-400 dark:text-slate-500">No email</span> }
                    </div>
                  </div>

                  <!-- Address -->
                  <div class="min-w-0 text-xs">
                    @if (r.street || r.city) {
                      <div class="truncate text-slate-700 dark:text-slate-200">
                        @if (r.houseNo) { <span class="font-medium">{{ r.houseNo }}</span> }
                        {{ r.street || '' }}
                      </div>
                      <div class="truncate text-slate-500 dark:text-slate-400">{{ place(r) }}</div>
                    } @else { <span class="text-slate-400 dark:text-slate-500">No address</span> }
                  </div>

                  <!-- TIN / VAT -->
                  <div class="min-w-0 text-xs">
                    @if (r.tin || r.vatNumber) {
                      @if (r.tin) { <div class="truncate"><span class="mr-1 font-medium text-slate-400">TIN</span><span class="font-mono text-slate-700 dark:text-slate-200">{{ r.tin }}</span></div> }
                      @if (r.vatNumber) { <div class="truncate"><span class="mr-1 font-medium text-slate-400">VAT</span><span class="font-mono text-slate-500 dark:text-slate-400">{{ r.vatNumber }}</span></div> }
                    } @else { <span class="text-slate-400 dark:text-slate-500">No TIN</span> }
                  </div>

                  <!-- Compliance (desktop) -->
                  <div class="hidden min-w-0 lg:block"><ng-container *ngTemplateOutlet="badge; context: { $implicit: r }"></ng-container></div>


                  <span class="flex items-center justify-start gap-1.5 lg:justify-end">
                    @if (r.complianceStatus === 'MissingDetails' || r.complianceStatus === 'Invalid') {
                      <span class="rounded-lg bg-primary-50 px-2 py-1 text-[11px] font-semibold text-primary-700 transition group-hover:bg-primary-100 dark:bg-primary-500/15 dark:text-brand-300">Fix<span class="lg:hidden"> details</span></span>
                    }
                    <svg class="hidden h-4 w-4 text-slate-400 transition-transform group-hover:translate-x-0.5 group-hover:text-primary-500 lg:block" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m9 18 6-6-6-6"/></svg>
                  </span>
                </a>
              </li>
            }
          </ul>
        </div>

        <app-pagination [page]="page()" [hasMore]="page() < totalPages()" [totalPages]="totalPages()" [loading]="loading()" (pageChange)="goToPage($event)"></app-pagination>
      }
    }

    <!-- Compliance badge -->
    <ng-template #badge let-r>
      @switch (r.complianceStatus) {
        @case ('Compliant') {
          <span class="inline-flex items-center gap-1 rounded-full bg-emerald-50 px-2.5 py-1 text-xs font-medium text-emerald-700 ring-1 ring-inset ring-emerald-500/20 dark:bg-emerald-500/10 dark:text-emerald-300">
            <svg class="h-3 w-3" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"><path fill-rule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clip-rule="evenodd"/></svg>
            Compliant
          </span>
        }
        @case ('MissingDetails') {
          <div class="min-w-0">
            <span class="inline-flex items-center gap-1 rounded-full px-2.5 py-1 text-xs font-medium ring-1 ring-inset"
                  [ngClass]="mode() === 'Lenient' ? 'bg-amber-50 text-amber-700 ring-amber-500/20 dark:bg-amber-500/10 dark:text-amber-300' : 'bg-rose-50 text-rose-700 ring-rose-500/20 dark:bg-rose-500/10 dark:text-rose-300'">
              <svg class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"/><path d="M12 8v4M12 16h.01"/></svg>
              {{ mode() === 'Lenient' ? 'Placeholders used' : 'Missing details' }}
            </span>
            <div class="mt-1 hidden truncate text-[11px] text-slate-500 lg:block dark:text-slate-400" [title]="'Missing: ' + r.missing.join(', ')">{{ r.missing.join(', ') }}</div>
          </div>
        }
        @case ('Invalid') {
          <div class="min-w-0">
            <span class="inline-flex items-center gap-1 rounded-full bg-rose-50 px-2.5 py-1 text-xs font-medium text-rose-700 ring-1 ring-inset ring-rose-500/20 dark:bg-rose-500/10 dark:text-rose-300">
              <svg class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M18 6 6 18M6 6l12 12"/></svg>
              {{ invalidLabel(r) }}
            </span>
            <div class="mt-1 hidden truncate text-[11px] text-slate-500 lg:block dark:text-slate-400" [title]="r.problems.join(' ')">{{ r.problems[0] }}</div>
          </div>
        }
        @default {
          <span class="inline-flex items-center gap-1 rounded-full bg-slate-100 px-2.5 py-1 text-xs font-medium text-slate-600 ring-1 ring-inset ring-slate-500/10 dark:bg-white/10 dark:text-slate-300">
            <span class="h-1.5 w-1.5 rounded-full bg-slate-400"></span>
            No TIN · cash sale
          </span>
        }
      }
    </ng-template>
  `
})
export class Customers implements OnInit {
  private service = inject(TaxDetailsService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);

  readonly rows = signal<CustomerTaxDetails[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly page = signal(1);
  readonly total = signal(0);
  readonly counts = signal<CustomerComplianceCounts>({ all: 0, compliant: 0, missingDetails: 0, invalid: 0, noTin: 0 });
  readonly countsLoaded = signal(false);
  readonly mode = signal<'Strict' | 'Lenient'>('Strict');
  readonly truncated = signal(false);
  readonly status = signal<StatusFilter>('');
  search = '';
  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.total() / PAGE_SIZE)));

  readonly tabs: { value: StatusFilter; label: string }[] = [
    { value: '', label: 'All' },
    { value: 'Attention', label: 'Needs attention' },
    { value: 'Compliant', label: 'Compliant' },
    { value: 'NoTin', label: 'No TIN' },
  ];

  readonly cards = computed(() => {
    const c = this.counts();
    const lenient = this.mode() === 'Lenient';
    return [
      { status: '' as StatusFilter, label: 'Customers', count: c.all, hint: 'Active in Xero', dot: 'bg-primary-500', activeRing: 'border-primary-300 ring-2 ring-primary-200 dark:border-primary-500/50 dark:ring-primary-500/20' },
      { status: 'Compliant' as StatusFilter, label: 'Compliant', count: c.compliant, hint: 'TIN + all buyer details', dot: 'bg-emerald-500', activeRing: 'border-emerald-300 ring-2 ring-emerald-200 dark:border-emerald-500/50 dark:ring-emerald-500/20' },
      { status: 'MissingDetails' as StatusFilter, label: 'Missing details', count: c.missingDetails, hint: lenient ? 'Placeholders will be used' : 'Will fail to fiscalize', dot: lenient ? 'bg-amber-500' : 'bg-rose-500', activeRing: 'border-amber-300 ring-2 ring-amber-200 dark:border-amber-500/50 dark:ring-amber-500/20' },
      { status: 'Invalid' as StatusFilter, label: 'Invalid TIN / VAT', count: c.invalid, hint: 'Blocked in every mode', dot: 'bg-rose-600', activeRing: 'border-rose-300 ring-2 ring-rose-200 dark:border-rose-500/50 dark:ring-rose-500/20' },
      { status: 'NoTin' as StatusFilter, label: 'No TIN', count: c.noTin, hint: 'Fiscalized as cash sales', dot: 'bg-slate-400', activeRing: 'border-slate-300 ring-2 ring-slate-200 dark:border-white/20 dark:ring-white/10' },
    ];
  });

  private readonly loadTrigger$ = new Subject<void>();

  constructor() {
    // switchMap drops the answer to an older request, so fast filter changes can't show stale rows.
    this.loadTrigger$.pipe(
      switchMap(() => this.service.customerOverview({ search: this.search || undefined, status: this.status(), page: this.page(), pageSize: PAGE_SIZE }).pipe(
        catchError((err) => {
          this.loading.set(false);
          this.error.set(extractErrorMessage(err, 'Failed to load customers.'));
          return EMPTY;
        })
      )),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe((res) => {
      this.rows.set(res.items ?? []);
      this.total.set(res.total ?? 0);
      this.counts.set(res.counts);
      this.countsLoaded.set(true);
      this.mode.set(res.validationMode === 'Lenient' ? 'Lenient' : 'Strict');
      this.truncated.set(!!res.truncated);
      this.loading.set(false);
      // The page we asked for may no longer exist after a filter change.
      if (this.page() > this.totalPages() && this.total() > 0) this.goToPage(this.totalPages());
    });
  }

  ngOnInit(): void {
    const q = this.route.snapshot.queryParamMap.get('status') as StatusFilter | null;
    if (q && STATUSES.includes(q)) this.status.set(q);
    this.load();
  }

  onSearch(text: string): void { this.search = text.trim(); this.page.set(1); this.load(); }
  reload(): void { this.load(); }
  goToPage(p: number): void { this.page.set(p); this.load(); }

  setStatus(s: StatusFilter): void {
    if (this.status() === s) return;
    this.status.set(s);
    this.page.set(1);
    this.router.navigate([], { relativeTo: this.route, queryParams: { status: s || null }, replaceUrl: true });
    this.load();
  }

  /** The list's filter, carried to the customer page so Previous / Next and "Save & next" walk the same list. */
  rowQuery(): Record<string, string> | null {
    const q: Record<string, string> = {};
    if (this.status()) q['queue'] = this.status();
    if (this.search) q['q'] = this.search;
    return Object.keys(q).length ? q : null;
  }

  invalidLabel(r: CustomerTaxDetails): string {
    const p = r.problems.join(' ');
    if (/no TIN/i.test(p)) return 'VAT without TIN';
    if (/^VAT/i.test(r.problems[0] ?? '')) return 'Invalid VAT';
    return 'Invalid TIN';
  }

  /** "Gweru, Midlands"; a city that is also its province (Harare, Bulawayo) is shown once. */
  place(r: CustomerTaxDetails): string {
    const parts = [r.city, r.province].filter((v): v is string => !!v && !!v.trim());
    return [...new Set(parts.map((p) => p.trim()))].join(', ') || '—';
  }

  initials(name: string | null | undefined): string { return customerInitials(name); }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.loadTrigger$.next();
  }
}
