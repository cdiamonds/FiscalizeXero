import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';

type PageSlot = number | 'gap-start' | 'gap-end';

/**
 * Previous / numbered pages / Next. With `total` and `pageSize` it also says "Showing 26–50 of 1,234 invoices", and
 * with `pageSizes` it offers a rows-per-page choice. Long ranges show the first and last page with gaps between.
 */
@Component({
  selector: 'app-pagination',
  imports: [CommonModule],
  template: `
    <nav class="flex flex-col gap-3 rounded-2xl border border-white/40 bg-white/70 p-3 shadow-sm backdrop-blur-sm pagination-slide-up sm:flex-row sm:items-center sm:justify-between dark:border-white/10 dark:bg-slate-900/60"
         aria-label="Pagination">
      @if (total != null && pageSize) {
        <div class="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 px-1 text-sm text-slate-600 sm:justify-start dark:text-slate-400">
          <p aria-live="polite">
            @if (total > 0) {
              Showing <span class="font-semibold tabular-nums text-slate-900 dark:text-white">{{ rangeStart() | number }}–{{ rangeEnd() | number }}</span>
              of <span class="font-semibold tabular-nums text-slate-900 dark:text-white">{{ total | number }}</span> {{ itemLabel }}
            } @else {
              No {{ itemLabel }}
            }
          </p>
          @if (pageSizes.length) {
            <label class="inline-flex items-center gap-2 whitespace-nowrap">
              <span class="text-slate-500 dark:text-slate-400">Rows</span>
              <select [value]="pageSize" (change)="onSize($event)" [disabled]="loading"
                      class="rounded-lg border border-slate-200 bg-white py-1 pl-2 pr-7 text-sm font-medium text-slate-700 focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-500/30 disabled:opacity-50 dark:border-white/10 dark:bg-slate-950/60 dark:text-slate-200">
                @for (s of pageSizes; track s) { <option [value]="s" [selected]="s === pageSize">{{ s }}</option> }
              </select>
            </label>
          }
        </div>
      }

      <div class="flex items-center justify-between gap-2 sm:justify-end">
        <button type="button" (click)="prev()" [disabled]="page <= 1 || loading" aria-label="Previous page"
                class="inline-flex items-center gap-1.5 rounded-xl border border-slate-200 bg-white/70 px-3 py-2 text-sm font-medium text-slate-700 transition-colors duration-200 hover:bg-white disabled:cursor-not-allowed disabled:opacity-50 dark:border-white/10 dark:bg-slate-950/60 dark:text-slate-300 dark:hover:bg-white/5">
          <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7"/></svg>
          <span class="hidden sm:inline">Previous</span>
        </button>

        <!-- Numbered pages (tablet and up) -->
        <div class="hidden items-center gap-1 md:flex">
          @for (p of slots(); track p) {
            @if (p === 'gap-start' || p === 'gap-end') {
              <span class="px-1.5 text-sm text-slate-400 dark:text-slate-500" aria-hidden="true">…</span>
            } @else {
              <button type="button" (click)="goTo(p)" [disabled]="loading" [attr.aria-current]="p === page ? 'page' : null" [attr.aria-label]="'Page ' + p"
                      class="min-w-[2.25rem] rounded-xl px-2.5 py-2 text-sm font-medium tabular-nums transition-colors duration-200 disabled:cursor-not-allowed"
                      [ngClass]="p === page
                        ? 'bg-gradient-to-r from-primary-500 to-primary-600 text-white shadow-md shadow-primary-500/25'
                        : 'border border-slate-200 bg-white/70 text-slate-700 hover:bg-white dark:border-white/10 dark:bg-slate-950/60 dark:text-slate-300 dark:hover:bg-white/5'">
                {{ p }}
              </button>
            }
          }
        </div>

        <!-- Phones: page x of y -->
        <div class="text-sm font-medium tabular-nums text-slate-600 md:hidden dark:text-slate-400">
          Page {{ page }}@if (totalPages) {<span> of {{ totalPages }}</span>}
        </div>

        <button type="button" (click)="next()" [disabled]="!hasMore || loading" aria-label="Next page"
                class="inline-flex items-center gap-1.5 rounded-xl border border-slate-200 bg-white/70 px-3 py-2 text-sm font-medium text-slate-700 transition-colors duration-200 hover:bg-white disabled:cursor-not-allowed disabled:opacity-50 dark:border-white/10 dark:bg-slate-950/60 dark:text-slate-300 dark:hover:bg-white/5">
          <span class="hidden sm:inline">Next</span>
          <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5l7 7-7 7"/></svg>
        </button>
      </div>
    </nav>
  `
})
export class Pagination {
  @Input({ required: true }) page = 1;
  @Input() hasMore = false;
  /** Total pages if known. When null, only prev/next + nearby pages are shown. */
  @Input() totalPages: number | null = null;
  @Input() loading = false;
  /** Total rows; with pageSize, shows "Showing x–y of total". */
  @Input() total: number | null = null;
  @Input() pageSize: number | null = null;
  /** Rows on this page (the last page can be short). Defaults to a full page. */
  @Input() pageCount: number | null = null;
  /** Rows-per-page choices; empty hides the picker. */
  @Input() pageSizes: number[] = [];
  /** Plural noun for the summary, e.g. "invoices". */
  @Input() itemLabel = 'items';
  @Output() pageChange = new EventEmitter<number>();
  @Output() pageSizeChange = new EventEmitter<number>();

  rangeStart(): number { return !this.total || !this.pageSize ? 0 : (this.page - 1) * this.pageSize + 1; }
  rangeEnd(): number {
    if (!this.total || !this.pageSize) return 0;
    const onPage = this.pageCount ?? this.pageSize;
    return Math.min(this.total, (this.page - 1) * this.pageSize + onPage);
  }

  /** Pages to show: a window around the current page, plus the first and last with gaps when they're far away. */
  slots(): PageSlot[] {
    const total = this.totalPages;
    const cur = this.page;
    if (!total || total <= 1) {
      const out: PageSlot[] = [];
      for (let i = Math.max(1, cur - 2); i <= (this.hasMore ? cur + 2 : cur); i++) out.push(i);
      return out;
    }
    if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
    let start = Math.max(2, cur - 1);
    let end = Math.min(total - 1, cur + 1);
    if (cur <= 3) { start = 2; end = 4; }
    if (cur >= total - 2) { start = total - 3; end = total - 1; }
    const out: PageSlot[] = [1];
    if (start > 2) out.push('gap-start');
    for (let i = start; i <= end; i++) out.push(i);
    if (end < total - 1) out.push('gap-end');
    out.push(total);
    return out;
  }

  /** Kept for callers that only want the numbers. */
  pageNumbers(): number[] { return this.slots().filter((s): s is number => typeof s === 'number'); }

  prev(): void { if (this.page > 1) this.pageChange.emit(this.page - 1); }
  next(): void { if (this.hasMore) this.pageChange.emit(this.page + 1); }
  goTo(p: number): void { if (p !== this.page) this.pageChange.emit(p); }
  onSize(ev: Event): void { this.pageSizeChange.emit(Number((ev.target as HTMLSelectElement).value)); }
}
