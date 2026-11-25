import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subscription, forkJoin, of, catchError, map } from 'rxjs';
import { TaxDetailsService, ItemHsCode } from '../../core/services/xero/tax-details.service';
import { HsCodesService } from '../../core/services/xero/hs-codes.service';
import { ClientConfigService } from '../../core/services/xero/client-config.service';
import { HsCodeLookupItem } from '../../core/models/xero.models';
import { ToastService } from '../../core/services/toast/toast.service';
import { extractErrorMessage } from '../../core/utils/http-error';
import { PageHeader } from '../../components/page-header/page-header';
import { SearchBar } from '../../components/search-bar/search-bar';
import { HsCodeDialog } from '../../components/hs-code-dialog/hs-code-dialog';
import { Pagination } from '../../components/pagination/pagination';
import { DocumentList, PAGE_SIZES } from '../../core/utils/document-list';

type Validation = 'idle' | 'validating' | 'valid' | 'invalid';
interface RowCheck { state: Validation; description: string }

@Component({
  selector: 'app-items',
  imports: [CommonModule, FormsModule, RouterLink, PageHeader, SearchBar, HsCodeDialog, Pagination],
  template: `
    <app-page-header title="Items" subtitle="Set the HS code for each item. ZIMRA requires one on every invoice line; items without their own code use the default HS code from Fiscalization settings." />

    <!-- Search + refresh card -->
    <div class="mb-4 rounded-2xl border border-white/40 bg-white/70 p-3 shadow-sm backdrop-blur-sm slide-down dark:border-white/10 dark:bg-slate-900/60">
      <div class="flex flex-col gap-2 sm:flex-row sm:items-center sm:gap-3">
        <app-search-bar class="block min-w-0 flex-1" placeholder="Search items by code, name or description…" [initialValue]="list.search()" (valueChange)="onSearch($event)"></app-search-bar>
        <button type="button" (click)="reload()" [disabled]="list.loading()"
                class="inline-flex items-center justify-center rounded-xl border border-slate-200 bg-white/70 px-3.5 py-2.5 text-slate-600 transition-all duration-200 hover:bg-white hover:scale-[1.02] focus:ring-2 focus:ring-primary-500/30 disabled:cursor-not-allowed disabled:opacity-50 dark:border-white/10 dark:bg-slate-950/60 dark:text-slate-300 dark:hover:bg-white/5"
                title="Refresh" aria-label="Refresh items">
          <svg class="h-5 w-5" [class.animate-spin]="list.loading()" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"/></svg>
        </button>
      </div>
    </div>

    <div class="mb-4 flex items-start gap-3 rounded-2xl border border-brand-200/60 bg-brand-50/60 px-4 py-3 text-xs leading-relaxed text-brand-800 fade-in dark:border-primary-500/20 dark:bg-primary-500/10 dark:text-brand-200" style="animation-delay: 0.1s">
      <svg class="mt-0.5 h-4 w-4 flex-shrink-0 text-primary-500 dark:text-brand-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4M12 8h.01"/></svg>
      <span>Type a code or pick one from the HS code catalogue with Browse.
        Codes are saved per Xero item code and kept in Fiscalize (Xero has no box for them). A line with no Xero item uses the default HS code.
      </span>
    </div>

    <div *ngIf="!loading() && settingsLoaded() && rows().length > 0"
         class="mb-4 flex flex-col gap-2 rounded-2xl border border-slate-200/70 bg-white/70 px-4 py-3 text-xs text-slate-600 fade-in sm:flex-row sm:items-center sm:justify-between dark:border-white/10 dark:bg-slate-900/60 dark:text-slate-300">
      <span>Items without a code of their own fiscalize under the default HS code <span class="font-mono font-semibold text-slate-800 dark:text-white">{{ defaultHsCode() }}</span>.</span>
      <a routerLink="/fiscalization-settings" class="font-semibold text-primary-600 underline-offset-2 hover:underline dark:text-brand-400">Change the default</a>
    </div>

    <div *ngIf="!loading() && list.loaded()" class="mb-3 flex flex-wrap items-center gap-x-3 gap-y-2 text-sm text-slate-500 dark:text-slate-400 fade-in">
      <span>{{ list.total() | number }} {{ list.total() === 1 ? 'item' : 'items' }}<ng-container *ngIf="list.search()"> matching “<span class="font-medium text-primary-600 dark:text-brand-400">{{ list.search() }}</span>”</ng-container> · A–Z</span>
      <span *ngIf="draftCount() > 0" class="inline-flex items-center gap-2 rounded-full bg-amber-50 py-0.5 pl-2.5 pr-1 text-[11px] font-semibold text-amber-800 ring-1 ring-inset ring-amber-500/20 dark:bg-amber-500/10 dark:text-amber-200">
        <span class="inline-flex items-center gap-1.5"><span class="h-1.5 w-1.5 rounded-full bg-amber-500"></span>{{ draftCount() }} unsaved</span>
        <button type="button" (click)="saveAll()" [disabled]="savingAll() || !canSaveAll()"
                class="rounded-full bg-gradient-to-r from-primary-500 to-primary-600 px-2.5 py-0.5 text-[11px] font-semibold text-white shadow-sm transition hover:from-primary-600 hover:to-primary-700 disabled:opacity-50">
          {{ savingAll() ? 'Saving…' : 'Save all' }}
        </button>
      </span>
      <span *ngIf="missingCount() > 0" class="ml-auto inline-flex items-center gap-1.5 rounded-full bg-amber-100 px-2.5 py-0.5 text-[11px] font-semibold text-amber-700 dark:bg-amber-500/15 dark:text-amber-300">
        <span class="h-1.5 w-1.5 rounded-full bg-amber-500"></span>{{ missingCount() }} on this page without an HS code
      </span>
      <span *ngIf="missingCount() === 0 && rows().length > 0" class="ml-auto inline-flex items-center gap-1.5 rounded-full bg-emerald-100 px-2.5 py-0.5 text-[11px] font-semibold text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300">
        <span class="h-1.5 w-1.5 rounded-full bg-emerald-500"></span>Every item on this page has a code
      </span>
    </div>

    <div *ngIf="loading()" class="flex justify-center py-12">
      <div class="h-8 w-8 animate-spin rounded-full border-4 border-brand-200 border-t-primary-500"></div>
    </div>

    <ng-container *ngIf="!loading()">
      <div class="transition-opacity duration-200" [class.opacity-60]="list.loading()" [attr.aria-busy]="list.loading()">
      <!-- Empty -->
      <div *ngIf="rows().length === 0" class="rounded-2xl border border-white/40 bg-white/70 p-12 text-center shadow-sm backdrop-blur-sm empty-state-fade-in dark:border-white/10 dark:bg-slate-900/60">
        <div class="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-2xl bg-gradient-to-br from-slate-100 to-slate-200 text-slate-400 dark:from-white/5 dark:to-white/10 dark:text-slate-500">
          <svg class="h-8 w-8" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4"/></svg>
        </div>
        <h3 class="mb-2 text-lg font-medium text-slate-900 dark:text-white">No items found</h3>
        <p class="text-slate-600 dark:text-slate-400">{{ list.search() ? 'Try part of the code or name.' : 'No items in this Xero organisation yet.' }}</p>
      </div>

      <!-- Desktop table -->
      <div *ngIf="rows().length > 0" class="mb-4 hidden overflow-hidden rounded-2xl border border-white/40 bg-white/70 shadow-sm backdrop-blur-sm table-slide-up dark:border-white/10 dark:bg-slate-900/60 md:block">
        <div class="overflow-x-auto">
          <table class="w-full min-w-[36rem]">
            <thead class="bg-slate-50/50 dark:bg-white/5">
              <tr>
                <th class="px-3 py-3 xl:px-4 text-left text-xs font-medium uppercase tracking-wider text-slate-500 dark:text-slate-400">Item</th>
                <th class="hidden px-3 py-3 text-left text-xs font-medium uppercase tracking-wider text-slate-500 dark:text-slate-400 xl:table-cell">Item code</th>
                <th class="px-3 py-3 text-left text-xs font-medium uppercase tracking-wider text-slate-500 dark:text-slate-400">HS code</th>
                <th class="px-3 py-3 text-left text-xs font-medium uppercase tracking-wider text-slate-500 dark:text-slate-400">Status</th>
                <th class="px-3 py-3 xl:px-4 text-right text-xs font-medium uppercase tracking-wider text-slate-500 dark:text-slate-400">Actions</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-200/30 dark:divide-white/5">
              <tr *ngFor="let r of rows(); let i = index; trackBy: trackById"
                  class="transition-colors duration-200 hover:bg-slate-50/70 dark:hover:bg-white/5 table-row-fade-in"
                  [style.animation-delay]="(i < 10 ? i * 0.03 : 0.3) + 's'">
                <td class="px-3 py-2.5 xl:px-4">
                  <div class="flex items-center gap-3">
                    <span class="avatar-chip h-8 w-8">
                      <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4"/></svg>
                    </span>
                    <div class="min-w-0">
                      <div class="truncate text-sm font-medium text-slate-900 dark:text-white">{{ r.name }}</div>
                      <div class="font-mono text-[11px] text-slate-500 dark:text-slate-400 xl:hidden">{{ r.itemCode }}</div>
                    </div>
                  </div>
                </td>
                <td class="hidden whitespace-nowrap px-3 py-2.5 font-mono text-xs text-slate-500 dark:text-slate-400 xl:table-cell">{{ r.itemCode }}</td>
                <td class="px-3 py-2.5">
                  <ng-container *ngTemplateOutlet="hsInput; context: { $implicit: r }"></ng-container>
                </td>
                <td class="px-3 py-2.5">
                  <ng-container *ngTemplateOutlet="statusPill; context: { $implicit: r }"></ng-container>
                </td>
                <td class="px-3 py-2.5 xl:px-4 text-right">
                  <button type="button" class="btn-save" [disabled]="savingId() === r.itemCode || !canSave(r)" (click)="save(r)">
                    <svg *ngIf="savingId() === r.itemCode" class="h-3 w-3 animate-spin" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
                    {{ savingId() === r.itemCode ? 'Saving…' : 'Save' }}
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Mobile cards -->
      <div *ngIf="rows().length > 0" class="mb-4 space-y-4 md:hidden">
        <div *ngFor="let r of rows(); let i = index; trackBy: trackById"
             class="card-hover mobile-card-slide-up rounded-2xl border border-white/40 bg-white/70 p-4 shadow-sm backdrop-blur-sm dark:border-white/10 dark:bg-slate-900/60"
             [style.animation-delay]="(i < 10 ? i * 0.03 : 0.3) + 's'">
          <div class="mb-3 flex items-start justify-between gap-3">
            <div class="min-w-0">
              <h3 class="truncate font-semibold text-slate-900 dark:text-white">{{ r.name }}</h3>
              <p class="font-mono text-xs text-slate-500 dark:text-slate-400">{{ r.itemCode }}</p>
            </div>
            <ng-container *ngTemplateOutlet="statusPill; context: { $implicit: r }"></ng-container>
          </div>
          <span class="field-label">HS code</span>
          <ng-container *ngTemplateOutlet="hsInput; context: { $implicit: r }"></ng-container>
          <div class="mt-4 flex justify-end">
            <button type="button" class="btn-save" [disabled]="savingId() === r.itemCode || !canSave(r)" (click)="save(r)">
              <svg *ngIf="savingId() === r.itemCode" class="h-3 w-3 animate-spin" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
              {{ savingId() === r.itemCode ? 'Saving…' : 'Save' }}
            </button>
          </div>
        </div>
      </div>
      </div>

      <app-pagination *ngIf="list.total() > 0" class="mb-2 block"
                      [page]="list.page()" [totalPages]="list.totalPages()" [hasMore]="list.page() < list.totalPages()" [loading]="list.loading()"
                      [total]="list.total()" [pageSize]="list.pageSize()" [pageCount]="rows().length" [pageSizes]="pageSizes" itemLabel="items"
                      (pageChange)="list.goTo($event)" (pageSizeChange)="list.setPageSize($event)"></app-pagination>
    </ng-container>

    <ng-template #hsInput let-r>
      <div class="flex items-center gap-1.5">
        <input class="field font-mono min-w-[7.5rem] xl:min-w-[9rem]" [ngModel]="r.hsCode" (ngModelChange)="onCodeTyped(r, $event)" (keydown.enter)="save(r)"
               placeholder="e.g. 04021099" inputmode="numeric" [attr.aria-label]="'HS code for ' + r.name"
               [class.!border-emerald-400]="check(r).state === 'valid'" [class.!border-rose-400]="check(r).state === 'invalid'" />
        <button type="button" (click)="openPicker(r)" title="Browse the HS code catalogue" aria-label="Browse HS codes"
                class="inline-flex flex-shrink-0 items-center gap-1 rounded-lg border border-slate-200 bg-white/70 px-2 py-1.5 text-xs font-medium text-slate-600 transition hover:bg-white dark:border-white/10 dark:bg-white/5 dark:text-slate-300 dark:hover:bg-white/10">
          <svg class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>
          <span class="hidden xl:inline">Browse</span>
        </button>
      </div>
      <p *ngIf="check(r).state === 'validating'" class="mt-1 text-[11px] text-slate-500 dark:text-slate-400">Checking the catalogue…</p>
      <p *ngIf="check(r).state === 'valid'" class="mt-1 truncate text-[11px] font-medium text-emerald-600 dark:text-emerald-400">✓ {{ check(r).description || 'In the HS code catalogue' }}</p>
      <p *ngIf="check(r).state === 'invalid'" class="mt-1 text-[11px] font-medium text-rose-500">Not in the HS code catalogue. Pick one with Browse.</p>
      <p *ngIf="isDraft(r) && check(r).state !== 'invalid' && check(r).state !== 'validating'" class="mt-1 text-[11px] font-medium text-amber-600 dark:text-amber-400">Unsaved: press Save or Enter</p>
      <p *ngIf="check(r).state === 'idle' && !r.hsCode && r.hsCodeSource === 'Default' && defaultHsCode()" class="mt-1 text-[11px] text-slate-500 dark:text-slate-400">Using the company default <span class="font-mono">{{ defaultHsCode() }}</span>.</p>
    </ng-template>


    <app-hs-code-dialog [open]="pickingId() !== null" [initialSearch]="pickingSearch()" (close)="pickingId.set(null)" (picked)="onPicked($event)"></app-hs-code-dialog>

    <ng-template #statusPill let-r>
      <span *ngIf="!r.hsCode && r.hsCodeSource === 'Default'" class="inline-flex flex-shrink-0 items-center gap-1 whitespace-nowrap rounded-full bg-slate-100 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-slate-600 dark:bg-white/10 dark:text-slate-300">
        <span class="h-1.5 w-1.5 rounded-full bg-slate-400"></span>Company default
      </span>
      <span *ngIf="r.hsCode && r.hsCodeSource !== 'Default'" class="inline-flex flex-shrink-0 items-center gap-1 whitespace-nowrap rounded-full bg-emerald-100 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300"
            title="Kept in Fiscalize">
        <span class="h-1.5 w-1.5 rounded-full bg-emerald-500"></span>Saved
      </span>
      <span *ngIf="!r.hsCode && r.hsCodeSource !== 'Default' && !defaultHsCode()" class="inline-flex flex-shrink-0 items-center gap-1 whitespace-nowrap rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-amber-700 dark:bg-amber-500/15 dark:text-amber-300">
        <span class="h-1.5 w-1.5 rounded-full bg-amber-500"></span>Missing
      </span>
    </ng-template>
  `
})
export class Items implements OnInit, OnDestroy {
  private service = inject(TaxDetailsService);
  private hsCodes = inject(HsCodesService);
  private clientConfig = inject(ClientConfigService);
  private toast = inject(ToastService);

  readonly rows = signal<ItemHsCode[]>([]);
  /** Pages through Xero items by name; page, size and search live in the URL. */
  readonly list = new DocumentList<ItemHsCode>('/items', (q) => this.service.itemsPage(q), (items) => this.onLoaded(items),
    (err) => this.toast.error(extractErrorMessage(err, 'Failed to load items.')));
  readonly pageSizes = PAGE_SIZES;
  /** First load only; later pages keep the rows on screen (dimmed) while the next one arrives. */
  readonly loading = computed(() => this.list.loading() && !this.list.loaded());
  /** Codes typed but not saved, by item id: kept when paging or searching so nothing typed is lost. */
  readonly drafts = signal<Record<string, string>>({});
  readonly draftCount = computed(() => Object.keys(this.drafts()).length);
  readonly savingAll = signal(false);
  /** The code each row on screen had when it loaded (what a draft is compared with). */
  private originals = new Map<string, string>();
  readonly savingId = signal<string | null>(null);
  /** The company default from Fiscalization settings, when one is set. */
  readonly defaultHsCode = signal<string | null>(null);
  readonly settingsLoaded = signal(false);
  /** Catalogue check per row, keyed by item id. */
  readonly checks = signal<Record<string, RowCheck>>({});
  /** Row whose HS code the picker is choosing. */
  readonly pickingId = signal<string | null>(null);
  readonly pickingSearch = signal<string | null>(null);

  private readonly idle: RowCheck = { state: 'idle', description: '' };
  private validateTimers = new Map<string, ReturnType<typeof setTimeout>>();
  private validateSubs = new Map<string, Subscription>();

  ngOnInit(): void {
    this.list.init();
    this.clientConfig.getFiscalizationSettings().subscribe({
      next: (res) => { this.defaultHsCode.set(res?.defaultHsCode?.trim() || '04021099'); this.settingsLoaded.set(true); },
      error: () => { /* leave the default unknown; the banner stays hidden */ }
    });
  }

  ngOnDestroy(): void {
    this.validateTimers.forEach((t) => clearTimeout(t));
    this.validateSubs.forEach((s) => s.unsubscribe());
  }

  check(r: ItemHsCode): RowCheck { return this.checks()[r.itemCode] ?? this.idle; }

  canSave(r: ItemHsCode): boolean {
    const state = this.check(r).state;
    return state !== 'validating' && state !== 'invalid';
  }

  private setCheck(itemId: string, check: RowCheck): void {
    this.checks.update((c) => ({ ...c, [itemId]: check }));
  }

  onCodeTyped(r: ItemHsCode, value: string): void {
    r.hsCode = value;
    this.setDraft(r.itemCode, value);
    this.validateSubs.get(r.itemCode)?.unsubscribe();
    clearTimeout(this.validateTimers.get(r.itemCode));
    const code = value.trim();
    if (!code) { this.setCheck(r.itemCode, this.idle); return; }
    this.setCheck(r.itemCode, { state: 'validating', description: '' });
    this.validateTimers.set(r.itemCode, setTimeout(() => this.validate(r.itemCode, code), 400));
  }

  private validate(itemId: string, code: string): void {
    const sub = this.hsCodes.validate(code).subscribe({
      next: (res) => this.setCheck(itemId, res?.code ? { state: 'valid', description: res.description ?? '' } : { state: 'invalid', description: '' }),
      error: () => this.setCheck(itemId, { state: 'invalid', description: '' })
    });
    this.validateSubs.set(itemId, sub);
  }

  openPicker(r: ItemHsCode): void {
    this.pickingSearch.set(r.hsCode?.trim() || null);
    this.pickingId.set(r.itemCode);
  }

  onPicked(picked: HsCodeLookupItem): void {
    const id = this.pickingId();
    this.pickingId.set(null);
    if (!id) return;
    this.validateSubs.get(id)?.unsubscribe();
    clearTimeout(this.validateTimers.get(id));
    this.rows.update((rs) => rs.map((r) => (r.itemCode === id ? { ...r, hsCode: picked.code } : r)));
    this.setDraft(id, picked.code);
    this.setCheck(id, { state: 'valid', description: picked.description ?? '' });
  }

  onSearch(text: string): void { this.list.setSearch(text); }
  /** Refresh: read the items from Xero again. Unsaved codes stay. */
  reload(): void { this.list.reload(true); }

  isDraft(r: ItemHsCode): boolean { return r.itemCode in this.drafts(); }

  /** Remember a typed code unless it's back to what the item had. */
  private setDraft(itemId: string, value: string): void {
    const original = this.originals.get(itemId) ?? '';
    this.drafts.update((d) => {
      const next = { ...d };
      if ((value ?? '').trim() === original.trim()) delete next[itemId];
      else next[itemId] = value ?? '';
      return next;
    });
  }

  private clearDraft(itemId: string): void {
    this.drafts.update((d) => { const next = { ...d }; delete next[itemId]; return next; });
  }

  canSaveAll(): boolean {
    return Object.keys(this.drafts()).every((id) => { const s = this.checks()[id]?.state; return s !== 'invalid' && s !== 'validating'; });
  }

  /** Save every unsaved code, on this page and others. */
  saveAll(): void {
    const entries = Object.entries(this.drafts());
    if (!entries.length || !this.canSaveAll()) return;
    this.saveMany(Object.fromEntries(entries.map(([id, code]) => [id, code.trim() || null])));
  }

  /** Saves codes by Xero item code. */
  private saveMany(codes: Record<string, string | null>): void {
    const entries = Object.entries(codes);
    this.savingAll.set(true);
    forkJoin(entries.map(([id, code]) => this.service.saveItemHsCode(id, code).pipe(
      map(() => ({ id, ok: true, message: null as string | null })),
      catchError((err) => of({ id, ok: false, message: extractErrorMessage(err, 'Could not save.') }))
    ))).subscribe((results) => {
      this.savingAll.set(false);
      const saved = results.filter((r) => r.ok);
      saved.forEach((r) => this.clearDraft(r.id));
      const failed = results.filter((r) => !r.ok);
      if (saved.length) this.toast.success(`Saved ${saved.length} HS ${saved.length === 1 ? 'code' : 'codes'}.`);
      if (failed.length === 1) this.toast.error(`${failed[0].id}: ${failed[0].message}`);
      else if (failed.length) this.toast.error(`${failed.length} could not be saved. Check the codes marked below and try again.`);
      this.list.reload();
    });
  }

  trackById(_: number, r: ItemHsCode): string { return r.itemCode; }

  missingCount(): number { return this.rows().filter((r) => !r.hsCode && r.hsCodeSource !== 'Default').length; }

  /** A page arrived: show it with any unsaved codes typed earlier put back. */
  private onLoaded(res: ItemHsCode[]): void {
    const drafts = this.drafts();
    const rows = (res ?? []).map(this.normalize);
    this.originals = new Map(rows.map((r) => [r.itemCode, r.hsCode ?? '']));
    this.rows.set(rows.map((r) => (r.itemCode in drafts ? { ...r, hsCode: drafts[r.itemCode] } : r)));
    // Catalogue checks only matter for codes still being edited.
    this.checks.update((c) => Object.fromEntries(Object.entries(c).filter(([id]) => id in drafts)));
  }

  /** The API returns the code in use; the company default stays out of the input so it can't be saved as the item's own by accident. */
  private normalize = (r: ItemHsCode): ItemHsCode => r.hsCodeSource === 'Default' ? { ...r, hsCode: null } : r;

  save(row: ItemHsCode): void {
    if (!this.canSave(row)) return;
    const code = row.hsCode?.trim() || null;
    this.savingId.set(row.itemCode);
    this.service.saveItemHsCode(row.itemCode, code).subscribe({
      next: (saved) => {
        this.savingId.set(null);
        const fresh = this.normalize(saved);
        this.originals.set(saved.itemCode, fresh.hsCode ?? '');
        this.clearDraft(saved.itemCode);
        this.rows.update((rs) => rs.map((r) => (r.itemCode === saved.itemCode ? fresh : r)));
        this.setCheck(saved.itemCode, this.idle);
        if (saved.warning) this.toast.warning(saved.warning);
        else this.toast.success(code ? `Saved HS code for ${saved.name || saved.itemCode}.` : `${saved.name || saved.itemCode} now uses the default HS code.`);
      },
      error: (err) => {
        this.savingId.set(null);
        this.toast.error(extractErrorMessage(err, 'Could not save HS code.'));
      }
    });
  }
}
