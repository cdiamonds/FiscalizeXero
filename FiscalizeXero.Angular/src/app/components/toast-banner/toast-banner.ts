import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-toast-banner',
  imports: [CommonModule],
  template: `
    <div class="fade-in-down flex items-start gap-3 rounded-xl border px-4 py-3 text-sm"
         [ngClass]="{
           'border-emerald-200 bg-emerald-50 text-emerald-800 dark:border-primary-600/30 dark:bg-primary-600/10 dark:text-emerald-300': kind === 'success',
           'border-rose-200 bg-rose-50 text-rose-800 dark:border-rose-500/30 dark:bg-rose-500/10 dark:text-rose-300': kind === 'error',
           'border-amber-200 bg-amber-50 text-amber-800 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-300': kind === 'warning',
           'border-sky-200 bg-sky-50 text-sky-800 dark:border-sky-500/30 dark:bg-sky-500/10 dark:text-sky-300': kind === 'info'
         }">
      <div class="mt-0.5 flex h-5 w-5 flex-shrink-0 items-center justify-center">
        <svg *ngIf="kind === 'success'" class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M9 12l2 2 4-4"/></svg>
        <svg *ngIf="kind === 'error'" class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M15 9l-6 6M9 9l6 6"/></svg>
        <svg *ngIf="kind === 'warning'" class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
        <svg *ngIf="kind === 'info'" class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/></svg>
      </div>
      <div class="flex-1"><ng-content></ng-content></div>
    </div>
  `
})
export class ToastBanner {
  @Input() kind: 'success' | 'error' | 'warning' | 'info' = 'info';
}
