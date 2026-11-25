import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Dialog } from '../dialog/dialog';
import { HsCodesService } from '../../core/services/xero/hs-codes.service';
import { HsCodeLookupItem } from '../../core/models/xero.models';

/** Search-and-pick dialog over the Fiscalize HS code catalogue. Emits the chosen row and closes. */
@Component({
  selector: 'app-hs-code-dialog',
  imports: [CommonModule, FormsModule, Dialog],
  template: `
    <app-dialog [open]="open" title="Pick HS code" subtitle="Search the harmonised commodity code catalogue."
                [size]="'lg'" variant="gradient"
                iconPath="M11 4a7 7 0 100 14 7 7 0 000-14zM21 21l-4.35-4.35M11 8v6M8 11h6"
                (close)="close.emit()">
      <div class="space-y-3">
        <div class="relative">
          <svg class="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>
          <input type="text" [ngModel]="search()" (ngModelChange)="onSearch($event)"
                 placeholder="Search by code or description…" autofocus
                 class="w-full rounded-xl border border-slate-200 bg-white py-2.5 pl-9 pr-3 text-sm text-slate-900 outline-none transition-all duration-200 placeholder:text-slate-400 focus:border-primary-500 focus:ring-2 focus:ring-primary-500/30 dark:border-white/10 dark:bg-slate-950 dark:text-white" />
        </div>

        <div class="max-h-[420px] min-h-[200px] overflow-y-auto rounded-xl border border-slate-200/70 dark:border-white/5">
          <ul class="divide-y divide-slate-100/80 dark:divide-white/5">
            <li *ngIf="loading() && items().length === 0" class="px-4 py-12 text-center text-sm text-slate-500 dark:text-slate-400">
              <div class="mx-auto h-5 w-5 animate-spin rounded-full border-2 border-brand-200 border-t-primary-500"></div>
              <p class="mt-2">Searching…</p>
            </li>
            <li *ngIf="!loading() && error()" class="px-4 py-12 text-center text-sm text-rose-600 dark:text-rose-400">{{ error() }}</li>
            <li *ngIf="!loading() && !error() && items().length === 0" class="px-4 py-12 text-center text-sm text-slate-500 dark:text-slate-400">No HS codes found.</li>
            <li *ngFor="let it of items()"
                class="cursor-pointer px-4 py-2.5 transition-colors duration-150 hover:bg-brand-50/60 dark:hover:bg-primary-500/10"
                (click)="pick(it)">
              <div class="flex items-baseline gap-3">
                <span class="font-mono text-sm font-semibold text-slate-900 dark:text-white">{{ it.code }}</span>
                <span class="truncate text-xs text-slate-500 dark:text-slate-400">{{ it.description }}</span>
              </div>
            </li>
          </ul>
        </div>

        <div class="flex items-center justify-between text-xs text-slate-500 dark:text-slate-400">
          <span>{{ items().length }} / {{ totalCount() }} results</span>
          <button *ngIf="hasMore()" type="button" (click)="loadMore()" [disabled]="loading()"
                  class="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-xs font-medium text-slate-700 transition-all duration-200 hover:bg-slate-50 hover:scale-[1.03] disabled:opacity-50 dark:border-white/10 dark:bg-white/5 dark:text-slate-200 dark:hover:bg-white/10">
            <svg *ngIf="loading()" class="h-3 w-3 animate-spin" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
            Load more
          </button>
        </div>
      </div>
    </app-dialog>
  `
})
export class HsCodeDialog implements OnChanges {
  @Input() open = false;
  /** Pre-fills the search box when the dialog opens (e.g. the code typed so far). */
  @Input() initialSearch: string | null = null;
  @Output() close = new EventEmitter<void>();
  @Output() picked = new EventEmitter<HsCodeLookupItem>();

  private hsCodes = inject(HsCodesService);

  readonly search = signal('');
  readonly items = signal<HsCodeLookupItem[]>([]);
  readonly page = signal(1);
  readonly totalCount = signal(0);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  private debounce: ReturnType<typeof setTimeout> | null = null;
  private requestId = 0;

  readonly hasMore = () => this.items().length < this.totalCount();

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['open'] && this.open) {
      this.search.set(this.initialSearch?.trim() ?? '');
      this.load(true);
    }
  }

  onSearch(value: string): void {
    this.search.set(value);
    if (this.debounce) clearTimeout(this.debounce);
    this.debounce = setTimeout(() => this.load(true), 300);
  }

  loadMore(): void {
    this.page.update((p) => p + 1);
    this.load(false);
  }

  private load(reset: boolean): void {
    if (reset) {
      this.items.set([]);
      this.page.set(1);
    }
    const id = ++this.requestId;
    this.loading.set(true);
    this.error.set(null);
    this.hsCodes.search(this.search() || undefined, this.page(), 20).subscribe({
      next: (res) => {
        if (id !== this.requestId) return;
        this.items.update((cur) => reset ? (res.items ?? []) : [...cur, ...(res.items ?? [])]);
        this.totalCount.set(res.totalCount ?? 0);
        this.loading.set(false);
      },
      error: () => {
        if (id !== this.requestId) return;
        this.loading.set(false);
        this.error.set('Could not load HS codes. Check your Fiscalize session and try again.');
      }
    });
  }

  pick(item: HsCodeLookupItem): void {
    this.picked.emit(item);
    this.close.emit();
  }
}
