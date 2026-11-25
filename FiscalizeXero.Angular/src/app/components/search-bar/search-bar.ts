import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-search-bar',
  imports: [CommonModule, FormsModule],
  template: `
    <ng-container *ngIf="card; else inlineTpl">
      <div class="rounded-2xl border border-white/40 bg-white/70 p-3 shadow-sm backdrop-blur-sm slide-down dark:border-white/10 dark:bg-slate-900/60">
        <div class="flex flex-row gap-2 sm:gap-3">
          <div class="relative min-w-0 flex-1">
            <svg class="pointer-events-none absolute left-3 top-1/2 h-4.5 w-4.5 -translate-y-1/2 text-slate-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <circle cx="11" cy="11" r="7"></circle>
              <path d="m20 20-3.5-3.5"></path>
            </svg>
            <input type="text"
                   [ngModel]="value()"
                   (ngModelChange)="onInput($event)"
                   [placeholder]="placeholder"
                   class="w-full rounded-xl border border-slate-200 bg-white/80 py-2.5 pl-10 pr-10 text-sm text-slate-900 outline-none transition-all duration-200 placeholder:text-slate-400 focus:border-primary-500 focus:bg-white focus:ring-2 focus:ring-primary-500/30 dark:border-white/10 dark:bg-slate-950/60 dark:text-white dark:focus:bg-slate-950" />
            <button *ngIf="value()" type="button" (click)="clear()"
                    class="absolute right-2 top-1/2 -translate-y-1/2 rounded-md p-1 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-white/5 dark:hover:text-slate-200"
                    aria-label="Clear search">
              <svg class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6L6 18M6 6l12 12"/></svg>
            </button>
          </div>
          <button *ngIf="showRefresh" type="button" (click)="refresh.emit()" [disabled]="refreshing"
                  class="inline-flex items-center justify-center rounded-xl border border-slate-200 bg-white/80 px-3.5 py-2.5 text-slate-600 transition-all duration-200 hover:bg-white hover:scale-[1.02] focus:ring-2 focus:ring-primary-500/30 disabled:cursor-not-allowed disabled:opacity-50 dark:border-white/10 dark:bg-slate-950/60 dark:text-slate-300 dark:hover:bg-white/5"
                  title="Refresh">
            <svg class="h-4 w-4" [class.animate-spin]="refreshing" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"></path>
            </svg>
          </button>
          <ng-content></ng-content>
        </div>
      </div>
    </ng-container>

    <ng-template #inlineTpl>
      <div class="relative">
        <svg class="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <circle cx="11" cy="11" r="7"></circle>
          <path d="m20 20-3.5-3.5"></path>
        </svg>
        <input type="text"
               [ngModel]="value()"
               (ngModelChange)="onInput($event)"
               [placeholder]="placeholder"
               class="w-full rounded-xl border border-slate-200 bg-white/70 py-2.5 pl-9 pr-9 text-sm text-slate-900 outline-none backdrop-blur-sm transition-all duration-200 placeholder:text-slate-400 focus:border-primary-500 focus:bg-white focus:ring-2 focus:ring-primary-500/30 dark:border-white/10 dark:bg-slate-950/60 dark:text-white dark:focus:bg-slate-950" />
        <button *ngIf="value()" type="button" (click)="clear()"
                class="absolute right-2 top-1/2 -translate-y-1/2 rounded-md p-1 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-white/5 dark:hover:text-slate-200"
                aria-label="Clear search">
          <svg class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6L6 18M6 6l12 12"/></svg>
        </button>
      </div>
    </ng-template>
  `
})
export class SearchBar {
  @Input() placeholder = 'Search…';
  @Input() debounceMs = 400;
  @Input() card = false;
  @Input() showRefresh = false;
  @Input() refreshing = false;
  /** Text to show initially, e.g. a search restored from the URL. Doesn't emit valueChange. */
  @Input() set initialValue(v: string | null | undefined) { if (!this.touched) this.value.set(v ?? ''); }
  @Output() valueChange = new EventEmitter<string>();
  @Output() refresh = new EventEmitter<void>();

  readonly value = signal('');
  private timeout: any;

  private touched = false;

  onInput(v: string): void {
    this.touched = true;
    this.value.set(v);
    clearTimeout(this.timeout);
    this.timeout = setTimeout(() => this.valueChange.emit(v.trim()), this.debounceMs);
  }

  clear(): void {
    this.value.set('');
    clearTimeout(this.timeout);
    this.valueChange.emit('');
  }
}
