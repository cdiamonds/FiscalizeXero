import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ToastService } from '../../core/services/toast/toast.service';

@Component({
  selector: 'app-toast-container',
  imports: [CommonModule],
  template: `
    <div
      aria-live="assertive"
      aria-atomic="false"
      class="pointer-events-none fixed right-4 top-4 z-[9999] flex w-80 max-w-[calc(100vw-2rem)] flex-col gap-2">

      @for (toast of toastService.toasts(); track toast.id) {
        <div
          class="fade-in-down pointer-events-auto flex items-start gap-3 rounded-xl border px-4 py-3 text-sm shadow-lg shadow-slate-900/10 backdrop-blur-sm"
          [ngClass]="{
            'border-emerald-200 bg-emerald-50/95 text-emerald-800 dark:border-primary-600/30 dark:bg-emerald-900/60 dark:text-emerald-300': toast.kind === 'success',
            'border-rose-200 bg-rose-50/95 text-rose-800 dark:border-rose-500/30 dark:bg-rose-900/60 dark:text-rose-300': toast.kind === 'error',
            'border-amber-200 bg-amber-50/95 text-amber-800 dark:border-amber-500/30 dark:bg-amber-900/60 dark:text-amber-300': toast.kind === 'warning',
            'border-sky-200 bg-sky-50/95 text-sky-800 dark:border-sky-500/30 dark:bg-sky-900/60 dark:text-sky-300': toast.kind === 'info'
          }">

          <!-- Icon -->
          <div class="mt-0.5 flex h-5 w-5 flex-shrink-0 items-center justify-center">
            <svg *ngIf="toast.kind === 'success'" class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M9 12l2 2 4-4"/></svg>
            <svg *ngIf="toast.kind === 'error'"   class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M15 9l-6 6M9 9l6 6"/></svg>
            <svg *ngIf="toast.kind === 'warning'" class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
            <svg *ngIf="toast.kind === 'info'"    class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/></svg>
          </div>

          <span class="flex-1 leading-relaxed">{{ toast.message }}</span>

          <!-- Dismiss -->
          <button
            type="button"
            (click)="toastService.dismiss(toast.id)"
            class="-mr-1 -mt-0.5 flex h-6 w-6 flex-shrink-0 items-center justify-center rounded-lg opacity-60 transition-opacity hover:opacity-100"
            aria-label="Dismiss">
            <svg class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
          </button>
        </div>
      }
    </div>
  `
})
export class ToastContainer {
  readonly toastService = inject(ToastService);
}
